using System.Xml.Linq;
using Engine;
using Game;
using WorldRescue.Core;

namespace WorldRescue;

public sealed class RescueDialog : Dialog {
    readonly string m_directory;
    readonly RepairPlan m_plan;
    readonly Action m_continue, m_cancel;
    readonly Dictionary<Cell, int> m_choices = [];
    readonly List<(ButtonWidget Button, Action Click)> m_recordActions = [];
    readonly NeighborhoodWidget m_preview;
    readonly StackPanelWidget m_records;
    readonly Dictionary<EntityRecord, RecordPresentation> m_presentations = [];
    Task<Neighborhood>? m_previewTask;
    Task<RepairReceipt>? m_commit;
    int m_index, m_inspected;
    bool m_busy, m_confirming;
    string? m_previewError;

    public RescueDialog(string directory, RepairPlan plan, Action continueLoading, Action cancel) {
        m_directory = directory; m_plan = plan; m_continue = continueLoading; m_cancel = cancel;
        LoadContents(this, ContentManager.Get<XElement>("Dialogs/WorldRescueDialog"));
        m_preview = Children.Find<NeighborhoodWidget>("Preview");
        m_records = Children.Find<StackPanelWidget>("Records");
        Label("Title").Text = Texts.Get("世界修复助手", "World Rescue");
        Label("ChoiceTitle").Text = Texts.Get("点选记录，比较内容", "Choose a record to inspect");
        Label("SceneBadge").Text = Texts.Get("重叠位置", "Overlap");
        Label("Legend").Text = Texts.Get("拖动旋转 · 红框标记重叠", "Drag to rotate · Red marks overlap");
        Button("Cancel").Text = Texts.Get("取消", "Cancel");
        Button("Suggest").Text = Texts.Get("选择相同记录", "Choose identical");
        Button("Apply").Text = Texts.Get("备份并修复", "Back up & repair");
        ShowConflict();
    }

    LabelWidget Label(string name) => Children.Find<LabelWidget>(name);
    ButtonWidget Button(string name) => Children.Find<ButtonWidget>(name);

    public override void MeasureOverride(Vector2 available) {
        Size = new(Math.Min(1000, available.X - 24), Math.Min(700, available.Y - 24));
        float columnWidth = Math.Max(1, (Size.X - 48) / 2);
        Children.Find<CanvasWidget>("SceneColumn").Size = new(columnWidth, float.PositiveInfinity);
        Children.Find<CanvasWidget>("RecordsColumn").Size = new(columnWidth, float.PositiveInfinity);
        base.MeasureOverride(available);
    }

    public override void Update() {
        if (m_commit?.IsCompleted == true) FinishCommit();
        if (m_busy || m_confirming) return;
        if (m_previewTask?.IsCompleted == true) {
            if (m_previewTask.IsCompletedSuccessfully) {
                m_preview.Neighborhood = m_previewTask.Result;
                int value = m_preview.Neighborhood.Cells.FirstOrDefault(c => c.Position == m_preview.Neighborhood.Center)?.Value ?? 0;
                Label("BlockName").Text = Texts.BlockName(value);
                m_previewError = m_preview.Neighborhood.MissingChunks > 0 ? Texts.Get("部分周围方块无法预览", "Some surroundings unavailable") : null;
            }
            else {
                m_previewError = Texts.Get("无法预览周围方块", "Surroundings unavailable");
                Log.Warning($"[WorldRescue] Preview failed: {m_previewTask.Exception}");
            }
            m_previewTask = null;
        }
        Label("SceneBadge").Text = m_previewError ?? (m_preview.UnavailableModels > 0
            ? Texts.Get("线框表示无法预览的方块", "Wireframes: unavailable models")
            : Texts.Get("重叠位置", "Overlap"));
        Label("Status").Text = Texts.Get($"已选择 {m_choices.Count} / {m_plan.Conflicts.Count} 处 · 确认前不会修改世界",
            $"Chosen {m_choices.Count} / {m_plan.Conflicts.Count} · No changes until confirmation");
        Button("Previous").IsEnabled = m_index > 0;
        Button("Next").IsEnabled = m_index + 1 < m_plan.Conflicts.Count;
        Button("Apply").IsEnabled = m_choices.Count == m_plan.Conflicts.Count;
        Button("Suggest").IsEnabled = m_plan.Conflicts.Any(c => c.Equivalent && !m_choices.ContainsKey(c.Position));
        if (Button("Previous").IsClicked) { m_index--; ShowConflict(); }
        if (Button("Next").IsClicked) { m_index++; ShowConflict(); }
        if (Button("ZoomIn").IsClicked) m_preview.Zoom(-1);
        if (Button("ZoomOut").IsClicked) m_preview.Zoom(1);
        if (Button("Rotate").IsClicked) m_preview.Rotate();
        if (Button("Reset").IsClicked) m_preview.ResetView();
        if (Button("Context").IsClicked) m_preview.ShowSurroundings = !m_preview.ShowSurroundings;
        ((RescueIconButtonWidget)Button("Context")).Subtexture = ContentManager.Get<Subtexture>(
            "Textures/WorldRescue/Remix/" + (m_preview.ShowSurroundings ? "eye-line" : "eye-off-line"));
        UpdateToolHint();
        if (Button("Help").IsClicked) ShowHelp();
        if (Button("Cancel").IsClicked || Input.Cancel || Input.Back) { DialogsManager.HideDialog(this); m_cancel(); return; }
        if (Button("Suggest").IsClicked) {
            foreach (Conflict c in m_plan.Conflicts.Where(c => c.Equivalent)) m_choices.TryAdd(c.Position, 0);
            BuildRecords();
        }
        foreach (var action in m_recordActions.ToArray()) if (action.Button.IsClicked) { action.Click(); break; }
        if (Button("Keep").IsClicked) { m_choices[m_plan.Conflicts[m_index].Position] = m_inspected; BuildRecords(); }
        if (Button("Apply").IsClicked) Confirm();
    }

    void ShowConflict() {
        Conflict conflict = m_plan.Conflicts[m_index];
        m_inspected = m_choices.GetValueOrDefault(conflict.Position, 0);
        Label("Location").Text = Texts.Get($"位置 {m_index + 1}/{m_plan.Conflicts.Count} · {conflict.Position}", $"Location {m_index + 1}/{m_plan.Conflicts.Count} · {conflict.Position}");
        Label("BlockName").Text = Texts.Get("正在读取现场…", "Reading the scene…");
        m_preview.Neighborhood = new(conflict.Position, [], 0);
        m_preview.ResetView(); m_previewError = null;
        m_previewTask = Task.Run(() => RegionReader.Read(m_directory, conflict.Position));
        Children.Find<ScrollPanelWidget>("RecordTabsScroll").ScrollPosition = 0;
        BuildRecords();
    }

    void UpdateToolHint() {
        Label("Legend").Text = Texts.Get("拖动旋转 · 红框标记重叠", "Drag to rotate · Red marks overlap");
        foreach ((string name, string chinese, string english) in new[] {
            ("ZoomOut", "缩小现场", "Zoom out"), ("ZoomIn", "放大现场", "Zoom in"),
            ("Rotate", "转动视角", "Rotate view"), ("Reset", "重置视角", "Reset view"),
            ("Context", m_preview.ShowSurroundings ? "隐藏周围方块" : "显示周围方块", m_preview.ShowSurroundings ? "Hide surroundings" : "Show surroundings") }) {
            var button = (RescueIconButtonWidget)Button(name);
            Vector2 point = Input.MousePosition.HasValue ? button.ScreenToWidget(Input.MousePosition.Value) : new(-1);
            if (button.Clickable.IsPressed || (point.X >= 0 && point.Y >= 0 && point.X < button.ActualSize.X && point.Y < button.ActualSize.Y))
                Label("Legend").Text = Texts.Get(chinese, english);
        }
    }

    void BuildRecords() {
        m_records.Children.Clear(); m_recordActions.Clear();
        Children.Find<ScrollPanelWidget>("RecordsScroll").ScrollPosition = 0;
        StackPanelWidget tabs = Children.Find<StackPanelWidget>("RecordTabs");
        tabs.Children.Clear();
        Conflict conflict = m_plan.Conflicts[m_index];
        int kept = m_choices.GetValueOrDefault(conflict.Position, -1);
        for (int i = 0; i < conflict.Records.Count; i++) {
            int index = i;
            var button = new RescueIconButtonWidget { Name = $"Record{i}", Size = new(76, 72), Margin = new(2, 4),
                Subtexture = ContentManager.Get<Subtexture>("Textures/WorldRescue/Remix/file-copy-2-line"),
                Text = (i + 1).ToString(), IsChecked = m_inspected == i };
            if (kept == i) button.Children.Add(Icon("Textures/WorldRescue/Remix/check-line", 22, WidgetAlignment.Near, WidgetAlignment.Near, new(10, 10)));
            tabs.Children.Add(button);
            m_recordActions.Add((button, () => { m_inspected = index; BuildRecords(); }));
        }
        AddLabel(m_records, Texts.Get($"记录 {m_inspected + 1} · " + (conflict.Equivalent ? "内容相同" : "内容不同"),
            $"Record {m_inspected + 1} · " + (conflict.Equivalent ? "Identical contents" : "Different contents")));
        EntityRecord record = conflict.Records[m_inspected];
        if (!m_presentations.TryGetValue(record, out RecordPresentation? presentation))
            m_presentations[record] = presentation = RecordPresenters.Default.Describe(record, LanguageControl.CurrentLanguageName);
        foreach (DetailSection section in presentation.Sections) {
            var title = new StackPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new(0, 4) };
            title.Children.Add(Icon(section.Icon, 28, margin: new(4, 0)));
            title.Children.Add(new LabelWidget { Text = section.Title, VerticalAlignment = WidgetAlignment.Center });
            m_records.Children.Add(title);
            for (int start = 0; start < section.Items.Count; start += 4) {
                var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal };
                foreach (ItemStack item in section.Items.Skip(start).Take(4)) row.Children.Add(new RecordItemWidget(item));
                m_records.Children.Add(row);
            }
            foreach (RecordFact fact in section.Facts) {
                var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new(0, 4) };
                row.Children.Add(Icon(fact.Icon, 28, margin: new(4, 0)));
                row.Children.Add(new LabelWidget { Text = fact.Label + "  " + fact.Value, WordWrap = true,
                    VerticalAlignment = WidgetAlignment.Center });
                m_records.Children.Add(row);
            }
            if (section.Items.Count == 0 && section.Facts.Count == 0)
                AddLabel(m_records, section.EmptyText ?? Texts.Get("尚无说明", "No description available"), new(192, 192, 192));
        }
        if (presentation.HasUnexplainedContent || presentation.Sections.Count == 0) {
            var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new(0, 10) };
            row.Children.Add(Icon("Textures/WorldRescue/Remix/question-line", 28, margin: new(4, 0)));
            var content = new StackPanelWidget { Direction = LayoutDirection.Vertical };
            AddLabel(content, presentation.Sections.Count == 0 ? Texts.Get("内容尚无说明", "Contents have no description")
                : Texts.Get("部分内容尚无说明", "Some contents have no description"), new(255, 216, 150));
            AddLabel(content, Texts.Get("随所选记录完整保留", "Kept intact with the chosen record"), new(192, 192, 192));
            row.Children.Add(content); m_records.Children.Add(row);
        }
        if (conflict.Records.Select(r => r.Template).Distinct().Count() > 1)
            AddLabel(m_records, Texts.Get("这些记录的对象种类也不同", "These records also describe different object types"), new(255, 216, 150));
        Button("Keep").Text = kept == m_inspected ? Texts.Get($"已保留记录 {m_inspected + 1}", $"Keeping record {m_inspected + 1}")
            : Texts.Get($"保留记录 {m_inspected + 1}", $"Keep record {m_inspected + 1}");
        Button("Keep").IsEnabled = kept != m_inspected;
    }

    internal static RescueIconWidget Icon(string path, float size, WidgetAlignment horizontal = WidgetAlignment.Near,
        WidgetAlignment vertical = WidgetAlignment.Center, Vector2 margin = default) {
        Subtexture texture;
        try { texture = ContentManager.Get<Subtexture>(path); }
        catch { texture = ContentManager.Get<Subtexture>("Textures/WorldRescue/Remix/question-line"); }
        return new RescueIconWidget { Subtexture = texture, Size = new(size),
            HorizontalAlignment = horizontal, VerticalAlignment = vertical, Margin = margin, IsHitTestVisible = false };
    }
    static void AddLabel(StackPanelWidget parent, string text, Color? color = null) => parent.Children.Add(new LabelWidget {
        Text = text, WordWrap = true, Margin = new(0, 2), Color = color ?? Color.White
    });

    void ShowHelp() => DialogsManager.ShowDialog(ParentWidget, new MessageDialog(Texts.Get("如何选择", "How to choose"),
        Texts.Get("左侧是实际现场：红框标出重叠位置，周围方块半透明。\n下方图标依次是缩小、放大、旋转、复位、显示周围。\n右侧点开记录比较内容，再点“保留”。下划线表示正在查看，勾号表示将保留。\n未说明的内容不代表空白；所选记录会完整保留。“选择相同记录”只选择内容完全一致的重复项。",
            "The scene marks the overlap in red and fades its surroundings.\nTools: zoom out, zoom in, rotate, reset, show surroundings.\nOpen each record, compare, then Keep. An underline means viewing; a tick means keeping.\nUndescribed does not mean empty. The chosen record is preserved in full. Choose identical handles only exact duplicates."),
        Texts.Get("知道了", "OK"), null, null));

    void Confirm() {
        int count = m_plan.Conflicts.Sum(c => c.Records.Count - 1);
        m_confirming = true;
        DialogsManager.ShowDialog(ParentWidget, new MessageDialog(Texts.Get("确认修复", "Confirm repair"),
            Texts.Get($"将保留你选择的记录，移除另外 {count} 份。\n未保留记录中的内容不会合并。\n先保存原始备份，地形方块保持原样。", $"Keep your choices and remove {count} other records.\nTheir contents will not be merged.\nAn original backup is made first. Terrain stays unchanged."),
            Texts.Get("备份并修复", "Back up & repair"), Texts.Get("继续检查", "Review"), result => {
                m_confirming = false;
                if (result != MessageDialogButton.Button1) return;
                m_busy = true;
                Label("Status").Text = Texts.Get("正在备份并修复，请稍候…", "Backing up and repairing…");
                IsHitTestVisible = false;
                var choices = new Dictionary<Cell, int>(m_choices);
                m_commit = Task.Run(() => RepairTransaction.Commit(m_directory, m_plan, choices));
            }));
    }

    void FinishCommit() {
        Task<RepairReceipt> task = m_commit!; m_commit = null;
        IsHitTestVisible = true;
        if (task.IsFaulted) {
            m_busy = false;
            Log.Error($"[WorldRescue] Repair failed: {task.Exception}");
            DialogsManager.ShowDialog(ParentWidget, new MessageDialog(Texts.Get("修复未完成", "Repair not completed"),
                Texts.Get("未继续加载世界。可能有内容被其他对象引用、存档已变化或无法写入；请返回重试，并保留备份。", "Loading is stopped. Another object may reference this record, the world may have changed, or writing failed. Return and retry; keep the backup."), Texts.Get("知道了", "OK"), null, null));
            return;
        }
        RepairReceipt receipt = task.Result;
        DialogsManager.ShowDialog(ParentWidget, new MessageDialog(Texts.Get("修复完成，原始备份已保存", "Repaired. Original backup saved."),
            Texts.Get("重叠已消除，可以进入世界。也可以立即撤销这次修复。", "The overlaps are resolved. Enter the world, or undo this repair now."),
            Texts.Get("进入世界", "Enter world"), Texts.Get("撤销修复", "Undo repair"), result => {
                if (result == MessageDialogButton.Button1) { DialogsManager.HideDialog(this); m_continue(); }
                else {
                    try { RepairTransaction.Undo(m_directory, receipt); DialogsManager.HideDialog(this); m_cancel(); }
                    catch (Exception e) {
                        Log.Error($"[WorldRescue] Undo failed: {e}");
                        DialogsManager.ShowDialog(ParentWidget, new MessageDialog(Texts.Get("暂时无法撤销", "Undo unavailable"),
                            Texts.Get("世界可能已变化。修复前备份仍然保留，请返回世界列表。", "The world may have changed. The original backup is still available. Return to the world list."),
                            Texts.Get("返回", "Back"), null, _ => { DialogsManager.HideDialog(this); m_cancel(); }));
                    }
                }
            }));
    }
}

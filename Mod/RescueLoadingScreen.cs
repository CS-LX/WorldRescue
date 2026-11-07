using Engine;
using Game;
using WorldRescue.Core;

namespace WorldRescue;

public sealed class RescueLoadingScreen : GameLoadingScreen {
    enum Stage { Waiting, Scanning, Choosing, Ready, Leaving }
    Stage m_stage;
    Task<RepairPlan>? m_scan;

    public override void Enter(object[] parameters) {
        m_stage = Stage.Waiting;
        m_scan = null;
        base.Enter(parameters);
    }

    public override void Update() {
        if (m_stage is Stage.Choosing or Stage.Leaving) return;
        if (m_stage == Stage.Ready) { base.Update(); return; }
        if (m_stateMachine.CurrentState != "Loading") { base.Update(); return; }
        if (m_stage == Stage.Waiting) {
            m_stage = Stage.Scanning;
            ProgressManager.UpdateProgress(Texts.Get("正在检查世界…", "Checking world…"), 0f);
            string path = Storage.GetSystemPath(Storage.CombinePaths(m_worldInfo.DirectoryName, "Project.xml"));
            m_scan = Task.Run(() => new RepairPlan(File.ReadAllBytes(path)));
        }
        if (m_scan?.IsCompleted != true) return;
        if (m_scan.IsFaulted) {
            Log.Warning($"[WorldRescue] Scan failed: {m_scan.Exception}");
            m_stage = Stage.Choosing;
            DialogsManager.ShowDialog(this, new MessageDialog(Texts.Get("无法检查这个世界", "World check unavailable"),
                Texts.Get("未更改存档。你可以返回，或交给游戏尝试正常加载。", "Nothing was changed. Return, or try the normal game loader."),
                Texts.Get("返回", "Back"), Texts.Get("尝试加载", "Try loading"), result => {
                    if (result == MessageDialogButton.Button1) Cancel(); else m_stage = Stage.Ready;
                }));
            return;
        }
        RepairPlan plan = m_scan.Result;
        if (plan.Conflicts.Count == 0) { m_stage = Stage.Ready; return; }
        m_stage = Stage.Choosing;
        string directory = Storage.GetSystemPath(m_worldInfo.DirectoryName);
        DialogsManager.ShowDialog(this, new RescueDialog(directory, plan, () => m_stage = Stage.Ready, Cancel));
    }

    void Cancel() {
        m_stage = Stage.Leaving;
        ScreensManager.SwitchScreen("Play");
    }
}

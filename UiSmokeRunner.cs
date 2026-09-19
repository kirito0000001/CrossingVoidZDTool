using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CrossingVoidZDTool;

/// <summary>
/// 进程内 UI 冒烟：**用 automation peer 去点真实控件**，验证的是 XAML 接线本身。
///
/// 为什么需要它：接线下移（`Click=` → `Command=`）出错的症状是「点了没反应」——
/// 编译过得去、回归也全绿，只有真的点一下才知道。角色台那七个按钮还在
/// `DataTemplate` 里，模板里换绑定要跨 namescope，风险更高（C6b 的详情按钮就是这一类）。
/// 与其盲改，不如先有一条能点一遍的信号。
///
/// 它不碰鼠标键盘，也不操纵桌面：启动的是本程序的窗口，点的是它自己的控件，
/// 结果写进文件，然后自己退出。用法：
///     "零境交错：ZD工具箱.exe" --ui-smoke "C:\path\ui-smoke.txt"
/// </summary>
internal static class UiSmokeRunner
{
    private const string ArgumentName = "--ui-smoke";

    /// <summary>命令行里给了输出路径就返回它，否则返回 null（正常启动）。</summary>
    public static string? TryGetReportPath()
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], ArgumentName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    /// <summary>
    /// 等界面加载完之后跑一遍脚本。全程只读界面 + 触发控件，不改用户数据：
    /// 冒烟期间只点「卡片」「关闭」这类不写盘的东西，涉及删除/导出的按钮一律不点。
    /// </summary>
    public static async Task RunAsync(MainWindow window, string reportPath)
    {
        var lines = new List<string>();
        var failures = 0;

        void Check(string name, bool passed, string detail = "")
        {
            if (!passed)
            {
                failures++;
            }

            lines.Add($"{(passed ? "PASS" : "FAIL")} {name}{(detail.Length == 0 ? string.Empty : " | " + detail)}");
        }

        try
        {
            var runtimeLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrossingVoidZDTool",
                "Logs",
                "runtime.log");
            await WaitAsync(TimeSpan.FromSeconds(6));

            var root = window.Content as FrameworkElement;
            Check("主窗口内容已挂载", root is not null);
            if (root is null)
            {
                return;
            }

            // 1) 角色台上的角色卡按钮（在 DataTemplate 里）必须点得动，
            //    而且点完之后「当前角色」要真的被选上——这正是 C6b 要动的那处接线。
            var candidates = FindAll<Button>(root)
                .Where(button => button.IsHitTestVisible && button.IsEnabled)
                .ToArray();
            Check("角色台上有可点击的按钮", candidates.Length > 0, $"找到 {candidates.Length} 个");

            // 角色卡按钮的判据用 CommandParameter：它绑的就是当前角色卡，
            // 而且这正是 C6b 要改成 Command 时会用到的同一个参数。
            var cards = candidates
                .Where(button => button.CommandParameter is CharacterCard)
                .Select(button => (Button: button, Card: (CharacterCard)button.CommandParameter!))
                .ToArray();
            Check("找到角色卡按钮", cards.Length > 0, $"共 {cards.Length} 张：{string.Join(",", cards.Select(item => item.Card.Code))}");

            // 让下一步能直接挑控件，而不是靠文案猜：把具名按钮倒出来。
            lines.Add(
                "INFO 具名按钮=" + string.Join(
                    ",",
                    candidates.Where(button => !string.IsNullOrEmpty(button.Name))
                        .Select(button => button.Name)
                        .Distinct(StringComparer.Ordinal)));

            // **点另一张卡**再断言角色换了——上一版点的是「当前已经是」的那张，
            // 点击前后都是同一个代号，等于没验出接线。
            var currentCode = window.CharacterDeskCurrentCode;
            var target = cards.FirstOrDefault(item =>
                !string.Equals(item.Card.Code, currentCode, StringComparison.OrdinalIgnoreCase));
            if (target.Button is not null)
            {
                Invoke(target.Button);
                await WaitAsync(TimeSpan.FromSeconds(3));
                var after = window.CharacterDeskCurrentCode;
                Check(
                    "点另一张角色卡之后当前角色真的换了",
                    string.Equals(after, target.Card.Code, StringComparison.OrdinalIgnoreCase),
                    $"点击前={currentCode ?? "<null>"} 目标={target.Card.Code} 点击后={after ?? "<null>"}");
            }
            else
            {
                // 台面上只有当前这一张卡时，验同一个按钮的真实效果。
                // 实测（runtime.log 里那行「[操作] 打开角色详情」）：卡片点击打开的是
                // 角色详情弹窗，不是草稿——所以断言按事实写，不按我以为的写。
                var card = cards[0];
                var logMark = CountLines(runtimeLog);
                Invoke(card.Button);
                await WaitAsync(TimeSpan.FromSeconds(3));
                Check(
                    "点角色卡之后打开角色详情",
                    window.IsCharacterDetailOpen,
                    $"卡={card.Card.Code} 详情打开={window.IsCharacterDetailOpen}");
                // 「点了没反应」要能说出理由：把这次点击期间程序自己写的日志带出来。
                foreach (var line in ReadLinesSince(runtimeLog, logMark).Take(20))
                {
                    lines.Add("INFO 日志 " + line);
                }
            }

            // 2) 详情弹窗开合：详情刚被点开，所以关闭按钮这次应该真的在树上。
            //    它是详情模板里的按钮——C6b 另一个高风险点。
            var detailHost = FindFirstByName(root, "CharacterDetailHost");
            // 关闭按钮在 XAML 里没有 x:Name，只有文案。**在详情宿主的子树里找**，
            // 范围锁死，免得又点错别的浮层的关闭。
            var detailButtons = detailHost is null ? [] : FindAll<Button>(detailHost).ToArray();
            lines.Add(
                "INFO 详情内按钮=" + string.Join(",", detailButtons.Select(DescribeLabel).Distinct(StringComparer.Ordinal)));

            // 详情里这五个按钮就是 C6b 要下移接线的对象：先把「它们在、而且可点」钉住，
            // 改接线时若哪一条断了（点了没反应/按钮消失），这里当场红。
            foreach (var expected in new[] { "继续编辑", "导出角色", "打开角色目录", "前往虚幻同步台", "查看角色" })
            {
                var match = detailButtons.FirstOrDefault(button =>
                    DescribeLabel(button).Contains(expected, StringComparison.Ordinal));
                Check(
                    $"详情按钮「{expected}」存在且可点",
                    match is not null && match.IsEnabled && match.IsHitTestVisible);
                // 接线翻成命令之后，「按钮在、点不动、不报错」这种形态就是命令为 null。
                // 光看 IsEnabled 抓不到它，所以这里多钉一条：命令必须挂上了。
                Check(
                    $"详情按钮「{expected}」已绑命令",
                    match?.Command is not null);
            }

            // 这个按钮是纯图标（没 x:Name、没文字），所以给它补了 AutomationProperties.Name
            // ——冒烟能认出来，辅助功能也顺手有了名字。
            var closeButton = detailButtons
                .FirstOrDefault(button => DescribeLabel(button).Contains("关闭", StringComparison.Ordinal));
            if (closeButton is not null)
            {
                Check(
                    "详情弹窗已展开",
                    detailHost is not null && detailHost.Visibility == Visibility.Visible);
                Invoke(closeButton);
                await WaitAsync(TimeSpan.FromSeconds(2));
                Check(
                    "点关闭之后详情弹窗收起",
                    detailHost is not null && detailHost.Visibility != Visibility.Visible);
            }
            else
            {
                // 详情里没有带「关闭」字样的按钮（大概是图标按钮，或只能点浮层外关闭）。
                // 这不是失败，是场景还没覆盖到那一种关法——证据留在上面那行 INFO 里。
                lines.Add("SKIP 详情弹窗关闭（详情内没有可识别的关闭按钮，见上面 INFO）");
            }

            // 3) St5 序列帧：进页面 → 点动作卡上的「管理」（**命令绑定**）→ 断言浮层打开
            //    → 点浮层里的关闭（同样是命令绑定）→ 断言收起。
            //    这一段是后面 34 个接线口的安全网：改坏了「命令绑不上」这里当场红。
            window.UiSmokeShowSt5Page();
            await WaitAsync(TimeSpan.FromSeconds(3));
            // 「元素在树上」不等于「页面已切换」——折叠页面的控件同样在树上，
            // 而且它们的 IsHitTestVisible 仍然是 true。所以：先按**可见性**确认页面切过来了，
            // 再把扫描范围限定在这个页面的子树里（上一版就是因为这两点没做，扫到了角色台的按钮）。
            var st5Page = FindFirstByName(root, "SequenceFramesPage");
            Check(
                "St5 页面已切换（按可见性判）",
                st5Page is not null && st5Page.Visibility == Visibility.Visible);
            // 注意作用域：**卡片按钮**在 St5 页面子树里，但**浮层**是窗口级元素
            // （和详情浮层一样挂在页面之外），所以浮层要从 root 找——上一版从这里找，
            // 于是「打开了却判成没打开」。
            var managerHost = FindFirstByName(root, "SequenceFramesManagerHost");

            var st5Buttons = st5Page is null
                ? []
                : FindAll<Button>(st5Page).Where(button => button.Visibility == Visibility.Visible).ToArray();
            // 认控件**按名字**，不按"第一个带 SequenceFrameSection 参数的按钮"——
            // 后者在别的卡片按钮也挂上命令之后会点错（2026-09-19 撞到过一次：
            // 卡片的播放按钮抢在前面，于是点了个不会开管理器的按钮）。
            // 纯图标按钮都补了 AutomationProperties.Name，就是给这里用的。
            var manageButton = st5Buttons
                .FirstOrDefault(button =>
                    button.Command is not null &&
                    button.CommandParameter is SequenceFrameSection &&
                    string.Equals(
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button),
                        "管理序列帧",
                        StringComparison.Ordinal));
            // 把 St5 页面自己的按钮倒出来：命令有没有绑上、参数是什么类型。
            lines.Add(
                "INFO St5 按钮=" + st5Buttons.Length + " 个：" + string.Join(
                    "; ",
                    st5Buttons
                        .Take(10)
                        .Select(button =>
                            $"{DescribeLabel(button)}[cmd={(button.Command is null ? "无" : "有")},param={button.CommandParameter?.GetType().Name ?? "null"}]")));
            if (manageButton is null)
            {
                lines.Add("SKIP St5「管理」按钮（这次没找到命令绑定的动作卡按钮）");
            }
            else
            {
                Invoke(manageButton);
                await WaitAsync(TimeSpan.FromSeconds(2));
                Check(
                    "点「管理」之后帧管理器打开",
                    managerHost is not null && managerHost.Visibility == Visibility.Visible);

                // 时间轴右键菜单 + 复用角标是**懒创建**的（右键才建 MenuFlyout），冒烟点不开它；
                // 但绑定路径就是**数据项上的属性**，所以直接对项断言"命令非空"。
                // 项上没命令的症状正是"菜单点了没反应、还不报错"——这一批最容易悄悄坏的地方。
                // 放在「管理」点开之后：这时时间轴才有帧（不然这一条只会 SKIP）。
                if (FindFirstByName(root, "SequenceFrameTimelineListView") is ListView timeline)
                {
                    var firstFrame = (timeline.ItemsSource as System.Collections.IEnumerable)
                        ?.Cast<object>()
                        .FirstOrDefault();
                    var itemMenuCommands = new[]
                    {
                        "ReplaceFrameMenuItemCommand",
                        "CopyFrameMenuItemCommand",
                        "InsertBlankBeforeMenuItemCommand",
                        "InsertBlankAfterMenuItemCommand",
                        "DeleteFrameMenuItemCommand",
                        "SelectReuseGroupCommand",
                        "PreviewThumbnailCommand"
                    };
                    var resolved = itemMenuCommands
                        .Select(name => (Name: name, Value: firstFrame?.GetType().GetProperty(name)?.GetValue(firstFrame)))
                        .ToArray();
                    lines.Add(
                        "INFO 时间轴项命令=" + string.Join(
                            ",",
                            resolved.Select(pair => $"{pair.Name}={(pair.Value is null ? "无" : "有")}")));
                    if (firstFrame is null)
                    {
                        lines.Add("SKIP 时间轴项命令（时间轴这次是空的）");
                    }
                    else
                    {
                        Check(
                            "时间轴项上的菜单命令都挂上了",
                            resolved.All(pair => pair.Value is not null));
                    }
                }

                // 编辑器那四个按钮（新建帧 / 左插入 / 右插入 / 复制）必须真的绑上命令。
                // **只查绑定、不点**——点「右插入」会真的往工程里插一帧空白帧，冒烟不该改用户数据。
                var editorButtons = managerHost is null
                    ? []
                    : FindAll<Button>(managerHost)
                        .Where(button => button.Visibility == Visibility.Visible)
                        .Select(button => (Label: DescribeLabel(button), Button: button))
                        .Where(pair => pair.Label is "新建帧" or "左插入" or "右插入" or "复制"
                            or "替换帧素材" or "从帧合集选择" or "删除当前帧")
                        .ToArray();
                foreach (var (label, button) in editorButtons)
                {
                    Check($"编辑器「{label}」已绑定命令", button.Command is not null);
                }

                if (editorButtons.Length == 0)
                {
                    lines.Add("SKIP 编辑器按钮检查（管理器浮层里没找到那四个按钮）");
                }

                // 浮层里可能不止一个命令按钮（管理器里就带了「帧素材合集」的入口），
                // 所以不能"取第一个当关闭"——那正是上一版点错的原因。
                // 依次试点，直到浮层收起：断言的是「存在一个命令按钮能关掉它」。
                var closeCandidates = managerHost is null
                    ? []
                    : FindAll<Button>(managerHost)
                        .Where(button => button.Command is not null)
                        .Take(6)
                        .ToArray();
                var closed = false;
                foreach (var candidate in closeCandidates)
                {
                    Invoke(candidate);
                    await WaitAsync(TimeSpan.FromSeconds(1));
                    if (managerHost!.Visibility != Visibility.Visible)
                    {
                        closed = true;
                        break;
                    }
                }

                if (closeCandidates.Length == 0)
                {
                    lines.Add("SKIP 帧管理器关闭（浮层里没找到命令绑定的关闭按钮）");
                }
                else
                {
                    Check("点关闭之后帧管理器收起", closed, $"试了 {closeCandidates.Length} 个命令按钮");
                }
            }

        }
        catch (Exception ex)
        {
            failures++;
            lines.Add($"FAIL 冒烟本身异常 | {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            lines.Add($"SUMMARY failures={failures}");
            try
            {
                File.WriteAllLines(reportPath, lines, new UTF8Encoding(false));
            }
            catch
            {
                // 报告写不出去也不能把冒烟变成崩溃。
            }

            window.Close();
        }
    }

    private static async Task WaitAsync(TimeSpan delay)
    {
        // 让出 UI 线程：等布局、绑定和异步加载跑完，而不是阻塞它。
        var completion = new TaskCompletionSource();
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            completion.TrySetResult();
        };
        timer.Start();
        await completion.Task;
    }

    private static void Invoke(Button button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button)
            ?? new ButtonAutomationPeer(button);
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            invoke.Invoke();
            return;
        }

        throw new InvalidOperationException($"按钮不可 Invoke：{DescribeLabel(button)}");
    }

    private static string DescribeLabel(Button button) =>
        (button.Content as string) ??
        (button.Content as TextBlock)?.Text ??
        AutomationProperties.GetName(button) ??
        button.Name ??
        "<无标签>";

    private static IEnumerable<T> FindAll<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>按 x:Name 找元素——模板外的固定控件用它比按文案猜稳。</summary>
    private static FrameworkElement? FindFirstByName(DependencyObject root, string name) =>
        FindAll<FrameworkElement>(root).FirstOrDefault(element => element.Name == name);

    private static int CountLines(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<string> ReadLinesSince(string path, int mark)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path).Skip(mark).ToArray() : [];
        }
        catch
        {
            return [];
        }
    }
}

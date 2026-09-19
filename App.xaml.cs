using Microsoft.UI.Xaml;

namespace CrossingVoidZDTool
{
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var window = new MainWindow();
            _window = window;
            _window.Activate();

            // 进程内 UI 冒烟（给详情按钮的接线用）：给了 --ui-smoke <报告路径> 就跑一遍
            // 真实控件的点击，写完报告自己退出；正常启动不受影响。
            if (UiSmokeRunner.TryGetReportPath() is { } reportPath)
            {
                _ = UiSmokeRunner.RunAsync(window, reportPath);
            }
        }
    }
}

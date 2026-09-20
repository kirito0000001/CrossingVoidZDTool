using System;
using System.Diagnostics;
using System.IO;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 用资源管理器打开一个文件夹（目录不存在就先建 —— "打开"一个不存在的目录不是谁想要的结果）。
        ///
        /// 全仓只有这一处「建目录 + <c>Process.Start</c>」：这段以前在七个 partial 里各抄了一份
        /// （序列帧动作目录 / 图集导出对话框 / 语音分类 / 角色详情 / 参考图 / BUFF / 基础素材）。
        /// 它**不吞异常**：调用方自己决定"打不开要不要让用户看见"（图集那里就要提示）。
        /// </summary>
        private static void OpenFolderInExplorer(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
        }
    }
}

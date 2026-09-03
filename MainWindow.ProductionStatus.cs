using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CrossingVoidZDTool
{
    public sealed partial class MainWindow
    {
        private void RefreshProductionStatusWithFeedback()
        {
            try
            {
                var status = _productionStatusService.Evaluate(CharacterDesk.CurrentCharacter);
                _applicationViewModel.ProductionStatus.Apply(status);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "制作状态检查失败", ex.Message);
                AppendLog(LogKind.Error, "制作状态检查失败。", ex);
            }
        }

        private async void CompleteCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterDesk.CurrentCharacter is null)
            {
                return;
            }

            try
            {
                await SaveDraftNowAsync();
                await SaveCharacterInfoNowAsync();
                await SaveSkillsNowAsync();
                await SaveBuffsNowAsync();
                var service = new Services.CharacterWorkspaceService();
                var completed = await Task.Run(() => service.SetCompleted(CharacterDesk.CurrentCharacter, true));
                CharacterDesk.ReplaceCharacter(completed);
                PersistCurrentCharacterSelection();
                RefreshProductionStatusWithFeedback();
                ShowCharacterDeskPage();
                ShowCharacterDetail(completed);
                ShowFloatingTip(InfoBarSeverity.Success, "角色制作已完成", completed.Name);
                AppendLog(LogKind.User, $"完成角色制作：{completed.Name} / {completed.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(InfoBarSeverity.Error, "完成制作失败", ex.Message);
                AppendLog(LogKind.Error, "完成制作失败。", ex);
            }
        }
    }
}

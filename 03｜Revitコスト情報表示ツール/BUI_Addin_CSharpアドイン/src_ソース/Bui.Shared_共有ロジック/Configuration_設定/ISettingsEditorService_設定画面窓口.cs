// このファイルは設定画面をJSONの保存方式から分離する契約です。
// BuiSettingsとOperationResultに依存し、将来DB設定が増えた場合は実装と画面の両方へ影響します。
using Bui.Common;

namespace Bui.Configuration;

public interface ISettingsEditorService
{
    Task<OperationResult<SettingsLoadResult>> LoadAsync(CancellationToken cancellationToken = default);
    Task<OperationResult<bool>> SaveLocalAsync(BuiSettings settings, CancellationToken cancellationToken = default);
}

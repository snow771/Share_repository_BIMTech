// このファイルは固定GUIDのProduct_IDとAssembly_IDを安全にRevitプロジェクトへ登録し、全カテゴリの結果を報告します。
// Revit 2025 DB API、Identifiers、同梱定義ファイル、API非依存の判定モデルに依存し、GUID・Binding種別・カテゴリ選定の変更は既存モデルの値に影響します。
using System.IO;
using Autodesk.Revit.DB;
using Bui.Products;
using Bui.Resources;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Bui.Revit.SharedParameters;

/// <summary>BUI固定ID用の共有項目を単一トランザクションで登録します。</summary>
public sealed class SharedParameterService
{
    private const string SharedParameterRelativePath =
        @"Resources_画像と定義\SharedParameters_共有パラメータ.txt";

    /// <summary>
    /// 旧Product_ID。2026-09-07以降は登録も書込みもしません。
    /// 既存モデルに残っている値を消さないため、定義だけ残しています。
    /// </summary>
    private static readonly ParameterSpec ProductSpec = new(
        "Product_ID",
        Identifiers.ProductIdParameterGuid,
        ParameterBindingKind.Instance);

    private static readonly ParameterSpec AssemblySpec = new(
        "Assembly_ID",
        Identifiers.AssemblyIdParameterGuid,
        ParameterBindingKind.Type);

    /// <summary>
    /// 層ごとのProduct_IDです。Identifiersの並び順（仕上→下地⑤）をそのまま使います。
    /// 旧Product_IDはここに含めません。BUIは今後書き込まないためです。
    /// ただしモデルからの削除も行いません。既存の値を消さないためです。
    /// </summary>
    private static readonly IReadOnlyList<ParameterSpec> LayerProductSpecs =
        Identifiers.LayerProductIdParameterNames
            .Select((name, index) => new ParameterSpec(
                name,
                Identifiers.LayerProductIdParameterGuids[index],
                ParameterBindingKind.Instance))
            .ToArray();

    /// <summary>既存値を保護しながらBUI固定IDを登録し、カテゴリごとの結果を返します。</summary>
    public ParameterBindingReport Install(Document document, RevitApplication application)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(application);

        var originalFilename = application.SharedParametersFilename;
        var categoryResults = new List<CategoryBindingResult>();
        var conflicts = new List<string>();

        try
        {
            application.SharedParametersFilename = ResolveSharedParameterPath();
            var definitionFile = application.OpenSharedParameterFile()
                ?? throw new IdentifierSetupException(
                    "BUIの共有項目定義ファイルをRevitが読み込めませんでした。\n"
                    + "配置先の Resources_画像と定義 フォルダーにある共有パラメータの定義ファイルが壊れている可能性があります。\n"
                    + "ファイル: " + application.SharedParametersFilename);
            var layerDefinitions = LayerProductSpecs
                .Select(spec => (Spec: spec, Definition: FindDefinition(definitionFile, spec)))
                .ToArray();
            var assemblyDefinition = FindDefinition(definitionFile, AssemblySpec);
            var productTargets = CollectProductCategories(document, categoryResults);
            var assemblyTargets = CollectAssemblyCategories(document, categoryResults);

            using var transaction = new Transaction(document, "BUI Product_ID / Assembly_ID 初期設定");
            try
            {
                if (transaction.Start() != TransactionStatus.Started)
                {
                    throw new IdentifierSetupException("Revitのトランザクションを開始できませんでした。他のコマンドが実行中でないか確認してください。");
                }

                foreach (var (spec, definition) in layerDefinitions)
                {
                    InstallOne(document, application, spec, definition, productTargets, categoryResults, conflicts);
                }

                InstallOne(document, application, AssemblySpec, assemblyDefinition, assemblyTargets, categoryResults, conflicts);

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    throw new IdentifierSetupException("Revitのトランザクションを確定できず、変更を保存しませんでした。");
                }
            }
            catch
            {
                // 予期しないRevit例外時は2項目を片方だけ残さず、開始前のモデルへ戻します。
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }

            return new ParameterBindingReport(categoryResults, conflicts);
        }
        finally
        {
            // Revit全体の利用者設定なので、成功・競合・例外のどの経路でも必ず元のファイルへ戻します。
            application.SharedParametersFilename = originalFilename;
        }
    }

    private static void InstallOne(
        Document document,
        RevitApplication application,
        ParameterSpec spec,
        ExternalDefinition shippedDefinition,
        IReadOnlyList<TargetCategory> targets,
        ICollection<CategoryBindingResult> categoryResults,
        ICollection<string> conflicts)
    {
        var bindings = document.ParameterBindings;
        var expectedElement = SharedParameterElement.Lookup(document, spec.Guid);
        var existingDefinition = expectedElement?.GetDefinition();
        var existingBinding = existingDefinition is null ? null : bindings.get_Item(existingDefinition);

        var nameConflict = FindNameConflict(document, bindings, spec, expectedElement, existingBinding);
        if (nameConflict is not null)
        {
            conflicts.Add(nameConflict);
            AddTargetResults(spec, targets, null, ParameterBindingAction.Conflict, categoryResults);
            return;
        }

        var existingKind = GetBindingKind(existingBinding);
        var hasAllCategories = existingBinding is ElementBinding elementBinding
            && targets.All(target => ContainsCategory(elementBinding.Categories, target.Category));
        var compatibleDefinition = existingDefinition is null
            || existingDefinition.GetDataType().Equals(SpecTypeId.String.Text);
        var decision = ParameterBindingDecision.Decide(
            spec.Name,
            spec.Guid,
            existingDefinition?.Name,
            expectedElement?.GuidValue,
            hasExpectedBinding: existingBinding is not null,
            hasAllRequiredCategories: hasAllCategories,
            expectedBindingKind: spec.BindingKind,
            existingBindingKind: existingKind,
            isCompatibleDefinition: compatibleDefinition);

        if (decision.Action == ParameterBindingAction.Conflict)
        {
            conflicts.Add($"{spec.Name}: {decision.Message} 既存データは変更していません。");
            AddTargetResults(spec, targets, existingBinding as ElementBinding, decision.Action, categoryResults);
            return;
        }

        if (decision.Action is ParameterBindingAction.CreateNew or ParameterBindingAction.ExtendCategories)
        {
            var categorySet = CreateUnionCategorySet(application, existingBinding, targets);
            var replacementBinding = CreateBinding(application, spec.BindingKind, categorySet);
            Definition bindingDefinition = existingDefinition is null
                ? shippedDefinition
                : existingDefinition;

            // 同じGUIDとBinding種別を保ったReInsertでカテゴリだけを和集合化し、既存要素の値を維持します。
            var changed = decision.Action == ParameterBindingAction.CreateNew
                ? bindings.Insert(bindingDefinition, replacementBinding, GroupTypeId.IdentityData)
                : bindings.ReInsert(bindingDefinition, replacementBinding, GroupTypeId.IdentityData);
            if (!changed)
            {
                throw new IdentifierSetupException(
                    $"共有項目「{spec.Name}」の登録（{decision.Action}）をRevitが拒否しました。\n"
                    + "同じ名前の項目がプロジェクトに既にないか確認してください。");
            }
        }

        AddTargetResults(spec, targets, existingBinding as ElementBinding, decision.Action, categoryResults);
    }

    private static string? FindNameConflict(
        Document document,
        BindingMap bindings,
        ParameterSpec spec,
        SharedParameterElement? expectedElement,
        Binding? expectedBinding)
    {
        var sameNameSharedParameters = new FilteredElementCollector(document)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>()
            .Where(element => string.Equals(element.GetDefinition().Name, spec.Name, StringComparison.Ordinal))
            .ToList();
        var differentGuid = sameNameSharedParameters.FirstOrDefault(element => element.GuidValue != spec.Guid);
        if (differentGuid is not null)
        {
            return $"{spec.Name}: 同じ名前で別のGUID「{differentGuid.GuidValue}」が存在するため、何も変更しませんでした。";
        }

        if (expectedElement is not null
            && !string.Equals(expectedElement.GetDefinition().Name, spec.Name, StringComparison.Ordinal))
        {
            return $"{spec.Name}: 固定GUIDの既存定義名が「{expectedElement.GetDefinition().Name}」のため、何も変更しませんでした。";
        }

        var sameNameBindingCount = 0;
        using var iterator = bindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (string.Equals(iterator.Key.Name, spec.Name, StringComparison.Ordinal))
            {
                sameNameBindingCount++;
            }
        }

        var expectedBindingCount = expectedBinding is null ? 0 : 1;
        return sameNameBindingCount > expectedBindingCount
            ? $"{spec.Name}: 同名の非共有または別定義のプロジェクト項目が存在するため、何も変更しませんでした。"
            : null;
    }

    private static IReadOnlyList<TargetCategory> CollectProductCategories(
        Document document,
        ICollection<CategoryBindingResult> categoryResults)
    {
        var targets = new List<TargetCategory>();
        var categories = document.Settings.Categories
            .Cast<Category>()
            .OrderBy(SafeCategoryName, StringComparer.CurrentCulture)
            .ToList();
        foreach (var category in categories)
        {
            try
            {
                if (!category.AllowsBoundParameters)
                {
                    categoryResults.Add(new CategoryBindingResult(
                        $"Product_ID / {category.Name}",
                        "対象外",
                        "Revitがプロジェクト項目の登録を許可していないカテゴリです。"));
                    continue;
                }

                targets.Add(new TargetCategory(category));
            }
            catch (Exception exception)
            {
                categoryResults.Add(new CategoryBindingResult(
                    $"Product_ID / {SafeCategoryName(category)}",
                    "対象外",
                    $"登録可否を確認できませんでした: {exception.Message}"));
            }
        }

        return targets;
    }

    private static IReadOnlyList<TargetCategory> CollectAssemblyCategories(
        Document document,
        ICollection<CategoryBindingResult> categoryResults)
    {
        var builtInCategories = new[]
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_Roofs
        };
        var targets = new List<TargetCategory>();
        foreach (var builtInCategory in builtInCategories)
        {
            try
            {
                var category = Category.GetCategory(document, builtInCategory);
                if (category is null || !category.AllowsBoundParameters)
                {
                    categoryResults.Add(new CategoryBindingResult(
                        $"Assembly_ID / {category?.Name ?? builtInCategory.ToString()}",
                        "対象外",
                        category is null
                            ? "このプロジェクトでカテゴリを取得できませんでした。"
                            : "Revitがプロジェクト項目の登録を許可していないカテゴリです。"));
                    continue;
                }

                targets.Add(new TargetCategory(category));
            }
            catch (Exception exception)
            {
                categoryResults.Add(new CategoryBindingResult(
                    $"Assembly_ID / {builtInCategory}",
                    "対象外",
                    $"カテゴリの取得または登録可否の確認に失敗しました: {exception.Message}"));
            }
        }

        return targets;
    }

    private static ExternalDefinition FindDefinition(DefinitionFile definitionFile, ParameterSpec spec)
    {
        foreach (DefinitionGroup group in definitionFile.Groups)
        {
            foreach (Definition definition in group.Definitions)
            {
                if (definition is ExternalDefinition externalDefinition && externalDefinition.GUID == spec.Guid)
                {
                    // Revitが読み取った名前をそのまま出します。文字化けしていれば定義ファイルの
                    // 文字コードが原因だと、この画面だけで判別できます。
                    if (!string.Equals(externalDefinition.Name, spec.Name, StringComparison.Ordinal))
                    {
                        throw new IdentifierSetupException(
                            $"共有項目の定義ファイルから読み取った名前が想定と違います。\n"
                            + $"想定: {spec.Name}\n"
                            + $"読取: {externalDefinition.Name}\n"
                            + "読取側が文字化けしている場合は、定義ファイルの文字コードがUTF-8（BOM付き）になっていません。");
                    }

                    if (!externalDefinition.GetDataType().Equals(SpecTypeId.String.Text))
                    {
                        throw new IdentifierSetupException($"共有項目「{spec.Name}」のデータ型がテキストではありません。");
                    }

                    return externalDefinition;
                }
            }
        }

        throw new IdentifierSetupException(
            $"共有項目の定義ファイルに「{spec.Name}」（GUID {spec.Guid}）がありません。\n"
            + "配置先のDLLと定義ファイルが古い可能性があります。Revitを終了してから再ビルドし、配置し直してください。");
    }

    private static CategorySet CreateUnionCategorySet(
        RevitApplication application,
        Binding? existingBinding,
        IEnumerable<TargetCategory> targets)
    {
        var categories = application.Create.NewCategorySet();
        if (existingBinding is ElementBinding elementBinding)
        {
            foreach (Category category in elementBinding.Categories)
            {
                categories.Insert(category);
            }
        }

        foreach (var target in targets)
        {
            if (!ContainsCategory(categories, target.Category))
            {
                categories.Insert(target.Category);
            }
        }

        return categories;
    }

    private static Binding CreateBinding(
        RevitApplication application,
        ParameterBindingKind bindingKind,
        CategorySet categories)
    {
        return bindingKind == ParameterBindingKind.Instance
            ? application.Create.NewInstanceBinding(categories)
            : application.Create.NewTypeBinding(categories);
    }

    private static ParameterBindingKind? GetBindingKind(Binding? binding)
    {
        return binding switch
        {
            InstanceBinding => ParameterBindingKind.Instance,
            TypeBinding => ParameterBindingKind.Type,
            _ => null
        };
    }

    private static bool ContainsCategory(CategorySet categories, Category target)
    {
        foreach (Category category in categories)
        {
            if (category.Id.Equals(target.Id))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddTargetResults(
        ParameterSpec spec,
        IEnumerable<TargetCategory> targets,
        ElementBinding? existingBinding,
        ParameterBindingAction action,
        ICollection<CategoryBindingResult> categoryResults)
    {
        foreach (var target in targets)
        {
            var wasAlreadyBound = existingBinding is not null
                && ContainsCategory(existingBinding.Categories, target.Category);
            var status = action switch
            {
                ParameterBindingAction.Conflict => "未変更",
                ParameterBindingAction.AlreadyCorrect => "設定済み",
                _ when wasAlreadyBound => "設定済み",
                _ => "追加"
            };
            var resultMessage = status switch
            {
                "未変更" => "定義が競合したため、既存データを保護して変更しませんでした。",
                "設定済み" => "正しいGUIDと登録種別で対象に含まれています。",
                _ => "正しいGUIDと登録種別で対象に追加しました。"
            };
            categoryResults.Add(new CategoryBindingResult(
                $"{spec.Name} / {target.Category.Name}",
                status,
                resultMessage));
        }
    }

    private static string ResolveSharedParameterPath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(SharedParameterService).Assembly.Location)
            ?? throw new IdentifierSetupException("BUIのDLLがどのフォルダーから読み込まれたか取得できませんでした。");
        var path = Path.Combine(assemblyDirectory, SharedParameterRelativePath);
        if (!File.Exists(path))
        {
            throw new IdentifierSetupException(
                "BUIの共有項目定義ファイルが配置されていません。\n"
                + "ファイル: " + path + "\n"
                + "Revitを終了してから再ビルドし、配置し直してください。");
        }

        RequireUtf8ByteOrderMark(path);
        return path;
    }

    /// <summary>
    /// 定義ファイルがUTF-8（BOM付き）であることを確かめます。
    /// RevitはBOMが無いファイルをWindowsの既定文字コードとして読むため、
    /// 「Product_ID（仕上）」のような日本語名がそのままでは文字化けし、
    /// GUIDは一致するのに名前が一致しない、という分かりにくい失敗になります。
    /// BOMを消してファイルを保存し直すと再発するので、ここで先に止めます。
    /// </summary>
    private static void RequireUtf8ByteOrderMark(string path)
    {
        var head = new byte[3];
        int read;
        using (var stream = File.OpenRead(path))
        {
            read = stream.Read(head, 0, head.Length);
        }

        if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            return;
        }

        throw new IdentifierSetupException(
            "BUIの共有項目定義ファイルの文字コードが UTF-8（BOM付き）ではありません。\n"
            + "このままではRevitが日本語のパラメータ名を正しく読めません。\n"
            + "ファイル: " + path);
    }

    private static string SafeCategoryName(Category category)
    {
        try
        {
            return category.Name;
        }
        catch
        {
            return $"カテゴリID {category.Id}";
        }
    }

    private sealed record ParameterSpec(string Name, Guid Guid, ParameterBindingKind BindingKind);
    private sealed record TargetCategory(Category Category);
}

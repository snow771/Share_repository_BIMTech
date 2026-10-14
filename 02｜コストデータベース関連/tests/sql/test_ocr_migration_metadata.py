"""Static regression checks; SQL Server execution is a separate integration check.

Run from SimilaritySearchApp: python -m unittest discover -s tests/sql -v
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / 'docs' / '2026-10-13_OCR見積DB連携'
MIGRATION = PACKAGE / 'OCR見積取決め_DB構造作成拡張.sql'
VERIFIER = PACKAGE / 'OCR見積取決め_DB構造確認.sql'


def temp_columns(path):
    sql = path.read_text(encoding='utf-8-sig')
    result = {}
    for match in re.finditer(r'CREATE TABLE\s+(#\w+)\s*\((.*?)\);', sql, re.S | re.I):
        definitions = re.split(r',\s*(?![^()]*\))', match[2])
        result[match[1]] = {
            definition.strip().split()[0].strip('[]'): definition.strip()
            for definition in definitions
        }
    return result


class MetadataNullabilityTests(unittest.TestCase):
    def test_verifier_delimits_rowcount_alias(self):
        sql = VERIFIER.read_text(encoding='utf-8-sig')
        self.assertTrue(re.search(r'COUNT_BIG\(\*\)\s+AS\s+\[RowCount\]', sql),
                        'RowCount must be a delimited SQL alias')

    def test_verifier_does_not_print_completion_after_dynamic_sql_error(self):
        sql = VERIFIER.read_text(encoding='utf-8-sig')
        self.assertTrue('BEGIN TRY' in sql and 'BEGIN CATCH' in sql,
                        'Verifier needs error handling for dynamic SQL')
        self.assertLess(sql.index('BEGIN TRY'), sql.index('EXEC sys.sp_executesql @Sql;'))
        catch = sql[sql.index('BEGIN CATCH'):sql.index('END CATCH;')]
        self.assertTrue('THROW;' in catch, 'SQL errors must propagate')
        self.assertLess(sql.index('END CATCH;'), sql.index("PRINT N'確認終了"))

    def test_existing_app_reference_columns_are_used(self):
        sql = MIGRATION.read_text(encoding='utf-8-sig')
        self.assertIn("N'ProjectInformation_物件情報',N'Project_id')", sql)
        self.assertIn("N'Category_カテゴリマスタ',N'Work_Type_Code')", sql)

    def test_reported_legacy_types_are_preserved_without_changing_fk_types(self):
        sql = MIGRATION.read_text(encoding='utf-8-sig')
        self.assertIn('CREATE TABLE #LegacyTypes', sql)
        self.assertIn('SET TypeSql=l.LegacyTypeSql', sql)
        self.assertIn('NOT EXISTS(SELECT 1 FROM #Refs r WHERE r.TableID=d.TableID AND r.ColumnName=d.ColumnName)', sql)

    def test_missing_category_group_has_separate_previewable_preparation(self):
        path = PACKAGE / 'カテゴリ分類マスタ_準備.sql'
        self.assertTrue(path.exists())
        sql = path.read_text(encoding='utf-8-sig')
        self.assertIn('DECLARE @Apply bit=0;', sql)
        self.assertIn('ROLLBACK TRANSACTION', sql)
        self.assertIn('TRY_CONVERT', sql)

    def test_preview_includes_problem_types_without_second_connection(self):
        sql = MIGRATION.read_text(encoding='utf-8-sig')
        preview = sql[:sql.index('IF @Apply=0')]
        self.assertIn('AS CurrentType', preview)
        self.assertIn('AS ExpectedType', preview)
        self.assertRegex(preview, r'FROM #Problems p\s+LEFT JOIN #Tables t')

    def test_every_temp_column_declares_nullability(self):
        # Prevent sysname/session defaults from deciding the staging schema.
        for path in (MIGRATION, VERIFIER):
            for table, columns in temp_columns(path).items():
                for column, definition in columns.items():
                    with self.subTest(file=path.name, table=table, column=column):
                        self.assertRegex(definition, r'\b(?:NULL|PRIMARY KEY)\b')

    def test_integer_keys_allow_missing_collation(self):
        tables = temp_columns(MIGRATION)
        for table in ('#External', '#Columns'):
            with self.subTest(table=table):
                definition = tables[table]['CollationName']
                self.assertRegex(definition, r'\bNULL\b')
                self.assertNotRegex(definition, r'\bNOT\s+NULL\b')

    def test_unresolved_metadata_can_be_staged(self):
        tables = temp_columns(MIGRATION)
        optional = {
            '#External': ('ObjectID', 'TypeSql', 'CollationName'),
            '#Tables': ('ObjectID',),
            '#Columns': ('DefaultSql', 'CollationName', 'ActualColumnID'),
            '#Refs': ('ParentFull', 'ParentObjectID'),
            '#Indexes': ('FilterSql',),
        }
        for table, names in optional.items():
            for name in names:
                with self.subTest(table=table, column=name):
                    definition = tables[table][name]
                    self.assertRegex(definition, r'\bNULL\b')
                    self.assertNotRegex(definition, r'\bNOT\s+NULL\b')


if __name__ == '__main__':
    unittest.main()

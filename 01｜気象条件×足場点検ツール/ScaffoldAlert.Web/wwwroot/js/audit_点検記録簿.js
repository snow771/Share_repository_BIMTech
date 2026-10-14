// ============================================================
//  点検記録簿の動き
// ============================================================
//  ・行を押すと、指示と点検の内容をモーダルで出す
//  ・見出しを押すと、その列で並べ替える
//
//  中身はすべて画面(HTML)の data-〇〇 に入っている。
//  データベースへ問い合わせに行かないので、押してすぐ開く。
//
//  外部の部品は使っていない。素のJavaScriptだけ。
// ============================================================

(function () {
    "use strict";

    // 行き先の頭に付ける文字。
    // ふだんは空。デモ画面から開かれたときだけ "/Demo" が入る。
    var 頭 = window.APP_PREFIX || "";

    // ============================================================
    //  文字を安全にする
    // ============================================================
    // 現場名や指摘事項に < や & が入っていても画面が壊れないようにする。
    function escapeHtml(text) {
        return String(text === null || text === undefined ? "" : text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    // 空のときに「-」を出す。空欄のままだと、
    // 「入っていない」のか「読めていない」のかが分からない。
    function orDash(text) {
        var v = String(text === null || text === undefined ? "" : text).trim();
        return v.length > 0 ? escapeHtml(v) : "-";
    }

    // ============================================================
    //  1. 並べ替え
    // ============================================================
    // 見出しを押すと、その列で並べ替える。もう一度押すと逆順。
    //
    // 比べ方は見出しの data-sort で決めている。
    //   text … 文字として（あいうえお順）
    //   num  … 数字として（10 が 9 より後ろになるように）
    //   date … 日時として
    // これを分けないと、数字が「1, 10, 2」の順に並んでしまう。
    function setupSort(table) {
        if (!table) {
            return;
        }

        var heads = table.querySelectorAll("thead th[data-sort]");
        var body = table.querySelector("tbody");

        if (!body) {
            return;
        }

        Array.prototype.forEach.call(heads, function (th, index) {
            th.classList.add("audit-sortable");

            th.addEventListener("click", function () {
                // 同じ列をもう一度押したら逆順にする。
                var asc = th.dataset.dir !== "asc";

                // 印を全部消してから、押された列にだけ付ける。
                Array.prototype.forEach.call(heads, function (other) {
                    other.classList.remove("audit-sort-asc", "audit-sort-desc");
                    if (other !== th) {
                        delete other.dataset.dir;
                    }
                });

                th.dataset.dir = asc ? "asc" : "desc";
                th.classList.add(asc ? "audit-sort-asc" : "audit-sort-desc");

                var kind = th.dataset.sort;
                var rows = Array.prototype.slice.call(body.rows);

                rows.sort(function (a, b) {
                    var x = cellValue(a, index, kind);
                    var y = cellValue(b, index, kind);

                    // 空欄は必ず下に置く。
                    // 「未実施」が上に固まると、埋もれて見落とす。
                    if (x === null && y === null) { return 0; }
                    if (x === null) { return 1; }
                    if (y === null) { return -1; }

                    if (x < y) { return asc ? -1 : 1; }
                    if (x > y) { return asc ? 1 : -1; }
                    return 0;
                });

                // 並べ替えた順に置き直す。
                rows.forEach(function (row) {
                    body.appendChild(row);
                });
            });
        });
    }

    // 1つの升目を、比べられる形にする。空欄は null にする。
    function cellValue(row, index, kind) {
        var cell = row.cells[index];
        if (!cell) {
            return null;
        }

        var text = cell.textContent.trim();

        if (text.length === 0 || text === "-" || text === "未実施") {
            return null;
        }

        if (kind === "num") {
            var n = parseFloat(text.replace(/[^0-9.\-]/g, ""));
            return isNaN(n) ? null : n;
        }

        if (kind === "date") {
            // "2026-08-31 09:12" の形。
            // 文字のまま比べても日時の順になるので、そのまま返す。
            return text;
        }

        return text;
    }

    setupSort(document.getElementById("audit-table"));
    setupSort(document.getElementById("audit-skipped-table"));

    // ============================================================
    //  2. 詳細（モーダル）
    // ============================================================
    var modal = document.getElementById("audit-detail");
    var title = document.getElementById("audit-detail-title");
    var body = document.getElementById("audit-detail-body");

    function closeDetail() {
        if (modal) {
            modal.classList.add("hidden");
        }
    }

    if (modal) {
        document.getElementById("audit-detail-close")
            .addEventListener("click", closeDetail);

        // 外側の暗いところを押しても閉じる。
        modal.addEventListener("click", function (e) {
            if (e.target === modal) {
                closeDetail();
            }
        });

        // Esc でも閉じる。
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape") {
                closeDetail();
            }
        });
    }

    // 表の1行ぶんの中身から、モーダルの中身を作る。
    function detailHtml(d) {
        var html = "";

        // ---------- どんな事象か ----------
        html += "<h3 class=\"audit-detail-head\">事象</h3>";
        html += "<table class=\"app-table audit-detail-table\"><tbody>";
        html += line("現場名", d.name);
        html += line("住所", d.addr);
        html += line("事象", escapeHtml(d.event) + " "
                            + orDash(d.value) + " " + escapeHtml(d.unit || ""));
        html += line("事象発生日時", d.occurred);
        html += line("根拠条文", d.legal);
        html += line("いまの状態", d.statusLabel);
        html += "</tbody></table>";

        // ---------- 指示 ----------
        html += "<h3 class=\"audit-detail-head\">指示</h3>";

        if (!d.instructedAt && !d.instructedBy) {
            html += "<p class=\"audit-detail-empty\">"
                  + "まだ指示の記録がありません。</p>";
        } else {
            html += "<table class=\"app-table audit-detail-table\"><tbody>";
            html += line("記録した日時", d.instructedAt);
            html += line(d.status === "NOT_REQUIRED" ? "判断者" : "指示者",
                         d.instructedBy);
            html += line(d.status === "NOT_REQUIRED" ? "対象外の理由"
                                                     : "指示内容",
                         d.instructionNote);
            html += "</tbody></table>";
        }

        // ---------- 点検 ----------
        // 対象外のものには点検の欄そのものを出さない。
        // 空の表が並ぶと「取れていないのか」と迷わせる。
        if (d.status !== "NOT_REQUIRED") {
            html += "<h3 class=\"audit-detail-head\">点検</h3>";

            if (!d.inspectedAt) {
                html += "<p class=\"audit-detail-empty audit-detail-warn\">"
                      + "まだ点検が実施されていません。"
                      + "臨検で指摘される可能性があります。</p>";
            } else {
                html += "<table class=\"app-table audit-detail-table\"><tbody>";
                html += line("点検実施日時", d.inspectedAt);
                html += line("事象からの日数",
                             d.days ? d.days + " 日" : "");
                html += line("点検実施者", d.inspector);
                html += line("点検者資格", d.qualify);
                html += line("足場種類", d.scaffold);
                html += line("点検結果", d.result);
                html += line("指摘事項", d.finding);
                html += line("登録した人", d.registered);
                html += line("登録した日時", d.insCreated);

                // あとから直された記録は、それが分かるようにする。
                //
                // ★臨検に出す記録なので、こっそり書き換わるのは良くない。
                //   点検は日を置いてから行うことがあり、
                //   あとから書き足すこと自体はふつうの運用。
                //   ただし「直っていること」が見えないのは別の話。
                if (d.insEdited === "1") {
                    html += "<tr><th>最終更新</th><td class=\"audit-detail-warn\">"
                          + orDash(d.insUpdated)
                          + "（登録のあとで直されています）</td></tr>";
                }

                html += "</tbody></table>";
            }
        }

        // ---------- 直す ----------
        // 直せるのは「指示済」と「対象外」だけ。
        // 点検が完了した記録は、臨検にそのまま出すものなので直せない。
        if (d.canEdit === "1") {
            html += "<div class=\"button-row\">"
                  + "<a class=\"save-btn\" href=\"" + 頭 + "/Alert/Edit?id="
                  + encodeURIComponent(d.alertId) + "\">"
                  + "この記録を直す</a></div>";
        } else {
            html += "<p class=\"audit-detail-empty\">"
                  + "まだ指示も点検も記録されていないため、"
                  + "直すところがありません。"
                  + "記録はアラート対応の画面から行ってください。</p>";
        }

        return html;
    }

    function line(name, value) {
        return "<tr><th>" + escapeHtml(name) + "</th>"
             + "<td>" + orDash(value) + "</td></tr>";
    }

    function openDetail(row) {
        if (!modal) {
            return;
        }

        var d = row.dataset;

        title.textContent = d.name || "記録の詳細";
        body.innerHTML = detailHtml({
            alertId: d.alertId,
            status: d.status,
            canEdit: d.canEdit,
            name: d.name,
            addr: d.addr,
            event: d.event,
            occurred: d.occurred,
            value: d.value,
            unit: d.unit,
            legal: d.legal,
            statusLabel: d.statusLabel,
            instructedAt: d.instructedAt,
            instructedBy: d.instructedBy,
            instructionNote: d.instructionNote,
            inspectedAt: d.inspectedAt,
            inspector: d.inspector,
            qualify: d.qualify,
            scaffold: d.scaffold,
            result: d.result,
            finding: d.finding,
            days: d.days,
            registered: d.registered,
            insCreated: d.insCreated,
            insUpdated: d.insUpdated,
            insEdited: d.insEdited
        });

        modal.classList.remove("hidden");
    }

    // 2つの表（対象の記録・対象外の記録）の両方で使えるようにする。
    function setupRows(table) {
        if (!table) {
            return;
        }

        var rows = table.querySelectorAll("tbody tr");

        Array.prototype.forEach.call(rows, function (row) {
            row.addEventListener("click", function () {
                Array.prototype.forEach.call(rows, function (other) {
                    other.classList.remove("audit-row-selected");
                });
                row.classList.add("audit-row-selected");

                openDetail(row);
            });
        });
    }

    setupRows(document.getElementById("audit-table"));
    setupRows(document.getElementById("audit-skipped-table"));
})();

// ============================================================
//  現場の詳細（モーダル）
// ============================================================
//  地図の丸か、アラート一覧の行を押すと開く。
//
//  ・左に  現場の情報 / 気象の実測値 / 観測された震度
//  ・右に  紐付いている観測所
//  観測所の行を押すと、その観測所の推移が観測所の表の下に出る。
//
//  推移を左の下ではなく観測所の表のすぐ下に出すのは、
//  選ぶたびに画面を送らないと結果が見えず、確認に手間がかかるため。
//
//  この呼び出し口を他のファイルからも使えるように、
//  window.SiteDetail という名前で外に出している。
// ============================================================

window.SiteDetail = (function () {
    "use strict";

    // 行き先の頭に付ける文字。
    // ふだんは空。デモ画面から開かれたときだけ "/Demo" が入る。
    // これで、本物とデモで同じ動きを使い回せる。
    var 頭 = window.APP_PREFIX || "";


    var modal = document.getElementById("site-detail");
    var title = document.getElementById("detail-title");
    var left = document.getElementById("detail-left");
    var right = document.getElementById("detail-right");

    // いま開いている現場のID。
    // 読み込んでいる途中で別の現場を押されたときに、
    // 古い方の結果で画面を上書きしないために使う。
    var openSiteId = null;

    // いま選んでいる観測所のID。選んでいなければ空。
    var selectedStationId = null;

    // 観測所の選びを外して、推移を消す。
    function clearTrend() {
        selectedStationId = null;

        var rows = right.querySelectorAll(".detail-stations tbody tr");
        Array.prototype.forEach.call(rows, function (row) {
            row.classList.remove("detail-row-selected");
        });

        var trend = document.getElementById("detail-trend");
        if (trend) {
            trend.innerHTML = "";
        }
    }

    // ============================================================
    //  開く・閉じる
    // ============================================================
    function open(siteId) {
        if (!modal) {
            return;
        }

        openSiteId = siteId;
        selectedStationId = null;
        title.textContent = "読み込んでいます...";
        left.innerHTML = "";
        right.innerHTML = "";
        modal.classList.remove("hidden");

        fetch(頭 + "/Home/SiteDetail?siteId="
              + encodeURIComponent(siteId))
            .then(function (res) { return res.json(); })
            .then(function (data) {
                // 読み込んでいる間に別の現場を押されたら、古い結果は捨てる。
                if (openSiteId !== siteId) {
                    return;
                }
                render(siteId, data);
            })
            .catch(function (e) {
                title.textContent = "現場の詳細";
                left.innerHTML = "<p class=\"detail-error\">"
                    + "読み込めませんでした: " + escapeHtml(e.message) + "</p>";
            });
    }

    function close() {
        if (modal) {
            modal.classList.add("hidden");
        }
        openSiteId = null;
    }

    if (modal) {
        document.getElementById("detail-close")
                .addEventListener("click", close);

        // 外側の暗いところを押したら閉じる。
        // 中身を押したときに閉じないよう、押された場所を確かめる。
        modal.addEventListener("click", function (e) {
            if (e.target === modal) {
                close();
            }
        });

        // Esc キーでも閉じられるようにする。
        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && !modal.classList.contains("hidden")) {
                close();
            }
        });

        // モーダルの中の、観測所の表と推移以外のところを押したら
        // 推移を消す。出したものを閉じる手立てが無いと、
        // 別の観測所を選ぶまで消せなくなる。
        //
        // 推移そのものを押したときは消さない。
        // 表を読んでいる途中で消えてしまうため。
        //
        // 見張りはここで1回だけ置く。モーダルを開くたびに置くと、
        // 開いた回数だけ重なってしまう。
        var content = modal.querySelector(".modal-content");
        if (content) {
            content.addEventListener("click", function (e) {
                if (e.target.closest(".detail-stations")
                    || e.target.closest("#detail-trend")) {
                    return;
                }
                clearTrend();
            });
        }
    }

    // ============================================================
    //  中身を組み立てる
    // ============================================================
    function render(siteId, data) {
        if (!data.found) {
            title.textContent = "現場の詳細";
            left.innerHTML = "<p class=\"detail-error\">"
                + (data.error
                   ? "読み込めませんでした: " + escapeHtml(data.error)
                   : "現場が見つかりませんでした。") + "</p>";
            return;
        }

        title.textContent = data.name;

        left.innerHTML =
            siteHeaderHtml(data) + weatherHtml(data) + quakeHtml(data);

        right.innerHTML = stationsHtml(data);

        bindStationRows(data);
    }

    // --- 現場の名前・部署・住所・所長 ---
    function siteHeaderHtml(data) {
        return "<dl class=\"detail-list\">"
             + "<dt>部署</dt><dd>" + escapeHtml(data.dept) + "</dd>"
             + "<dt>状態</dt><dd>" + escapeHtml(data.status) + "</dd>"
             + "<dt>住所</dt><dd>" + escapeHtml(data.addr) + "</dd>"
             + "<dt>現場所長</dt><dd>" + escapeHtml(data.manager) + "</dd>"
             + "</dl>";
    }

    // --- 気象の実測値 ---
    function weatherHtml(data) {
        var html = "<h5 class=\"detail-heading\">"
                 + "気象の実測値（直近3時間）</h5>";

        // 下に出す数値は「紐付いた複数の観測所のうち最大値」であって、
        // 最寄り観測所の値ではない。名前だけを置くと誤読されるため、
        // 「最寄りの観測所：」というラベルを必ず付ける。
        if (data.nearest) {
            var distance = (data.nearest.distance === null
                            || data.nearest.distance === undefined)
                ? ""
                : "（" + Number(data.nearest.distance).toFixed(1) + "km）";

            html += "<p class=\"detail-note\">最寄りの観測所："
                  + escapeHtml(data.nearest.name) + distance
                  + "　※下の数値はこの観測所の値ではありません。"
                  + "紐付いた複数の観測所のうち最も大きい値です。</p>";
        }

        if (!data.weather.hasAny) {
            html += "<p class=\"detail-empty\">"
                  + "直近3時間の観測データがありません。</p>";
            return html;
        }

        html += "<table class=\"app-table detail-table\"><thead><tr>"
              + "<th>12時間雨量</th><th>1時間雨量</th>"
              + "<th>最大風速</th><th>最大瞬間風速</th><th>観測時刻</th>"
              + "</tr></thead><tbody><tr>"
              + numberCell(data.weather.precip12h, "mm")
              + numberCell(data.weather.precip1h, "mm")
              + numberCell(data.weather.wind, "m/s")
              + numberCell(data.weather.gust, "m/s")
              + "<td>" + escapeHtml(data.weather.observedAt || "-") + "</td>"
              + "</tr></tbody></table>"
              + "<p class=\"detail-note\">"
              + "各値は別々の観測所・別々の時刻のものである場合があります。</p>";

        return html;
    }

    // --- この現場で観測された震度 ---
    function quakeHtml(data) {
        var html = "<h5 class=\"detail-heading\">"
                 + "この現場で観測された震度（直近7日）</h5>";

        if (data.quakes.length === 0) {
            html += "<p class=\"detail-empty\">"
                  + "直近7日にこの現場の市区町村で観測された地震はありません。"
                  + "</p>";
            return html;
        }

        html += "<table class=\"app-table detail-table\"><thead><tr>"
              + "<th>発生日時</th><th>震源地</th><th class=\"app-num\">震度</th>"
              + "</tr></thead><tbody>";

        data.quakes.forEach(function (q) {
            html += "<tr><td>" + escapeHtml(q.at) + "</td>"
                  + "<td>" + escapeHtml(q.place) + "</td>"
                  + "<td class=\"app-num\">" + escapeHtml(q.intensity)
                  + "</td></tr>";
        });

        return html + "</tbody></table>";
    }

    // --- 紐付いている観測所 ---
    function stationsHtml(data) {
        var html = "<h5 class=\"detail-heading\">紐付いている観測所</h5>";

        if (data.stations.length === 0) {
            html += "<p class=\"detail-error\">"
                  + "観測所が紐付いていません。"
                  + "この現場は大雨・強風の判定対象外です。</p>";
            return html;
        }

        html += "<p class=\"detail-note\">"
              + "行を押すと、その観測所の実測値の推移を確認できます。"
              + "もう一度押すか、この欄の外を押すと閉じます。</p>";

        html += "<table class=\"app-table detail-table detail-stations\">"
              + "<thead><tr>"
              + "<th>最寄</th><th>観測所</th><th>都道府県</th>"
              + "<th class=\"app-num\">距離km</th>"
              + "</tr></thead><tbody>";

        data.stations.forEach(function (s) {
            html += "<tr data-station-id=\"" + escapeHtml(s.id) + "\""
                  + " data-station-name=\"" + escapeHtml(s.name) + "\">"
                  + "<td>" + (s.nearest ? "★" : "") + "</td>"
                  + "<td>" + escapeHtml(s.name) + "</td>"
                  + "<td>" + escapeHtml(s.pref) + "</td>"
                  + numberCell(s.distance, "")
                  + "</tr>";
        });

        html += "</tbody></table>";

        // 推移はこの下に出す。最初は空。
        html += "<div id=\"detail-trend\"></div>";

        return html;
    }

    // --- 観測所の行を押したときの動き ---
    function bindStationRows(data) {
        var rows = right.querySelectorAll(".detail-stations tbody tr");
        var trend = document.getElementById("detail-trend");

        Array.prototype.forEach.call(rows, function (row) {
            row.addEventListener("click", function () {
                var id = row.dataset.stationId;

                // 選んでいる観測所をもう一度押したら、推移を消す。
                if (selectedStationId === id) {
                    clearTrend();
                    return;
                }

                selectedStationId = id;
                Array.prototype.forEach.call(rows, function (other) {
                    other.classList.remove("detail-row-selected");
                });
                row.classList.add("detail-row-selected");

                var name = row.dataset.stationName;

                trend.innerHTML = "<p class=\"detail-empty\">"
                                + "読み込んでいます...</p>";

                fetch(頭 + "/Home/StationTrend?stationId="
                      + encodeURIComponent(id))
                    .then(function (res) { return res.json(); })
                    .then(function (result) {
                        // 読み込んでいる間に選びが変わったら、古い結果は捨てる。
                        if (selectedStationId !== id) {
                            return;
                        }
                        trend.innerHTML = trendHtml(name, result);
                    })
                    .catch(function (e) {
                        if (selectedStationId !== id) {
                            return;
                        }
                        trend.innerHTML = "<p class=\"detail-error\">"
                            + "推移を読み込めませんでした: "
                            + escapeHtml(e.message) + "</p>";
                    });
            });
        });
    }

    // --- 観測所の推移 ---
    function trendHtml(name, result) {
        var html = "<h5 class=\"detail-heading\">"
                 + escapeHtml(name) + " 観測所の推移（直近24時間）</h5>";

        if (!result.rows || result.rows.length === 0) {
            return html + "<p class=\"detail-empty\">"
                        + "直近24時間の観測データがありません。</p>";
        }

        // 24行あってもモーダルが縦に伸びないよう、表の中だけで縦に送る。
        html += "<div class=\"detail-scroll\">"
              + "<table class=\"app-table detail-table\"><thead><tr>"
              + "<th>観測時刻</th>"
              + "<th class=\"app-num\">12時間雨量</th>"
              + "<th class=\"app-num\">1時間雨量</th>"
              + "<th class=\"app-num\">最大風速</th>"
              + "<th class=\"app-num\">最大瞬間風速</th>";

        // 雪の降らない時期は空欄になるだけなので、値が無ければ列ごと消す。
        if (result.hasSnow) {
            html += "<th class=\"app-num\">24時間降雪量</th>";
        }

        html += "</tr></thead><tbody>";

        result.rows.forEach(function (r) {
            html += "<tr><td>" + escapeHtml(r.at) + "</td>"
                  + numberCell(r.precip12h, "")
                  + numberCell(r.precip1h, "")
                  + numberCell(r.wind, "")
                  + numberCell(r.gust, "");
            if (result.hasSnow) {
                html += numberCell(r.snow, "");
            }
            html += "</tr>";
        });

        return html + "</tbody></table></div>";
    }

    // ============================================================
    //  小道具
    // ============================================================
    // 値が無いときは 0 ではなく「-」を出す。
    // 0 を出すと「降らなかった」と読めてしまい、
    // 「測れていない」と区別が付かなくなる。
    function numberCell(value, unit) {
        if (value === null || value === undefined) {
            return "<td class=\"app-num\">-</td>";
        }
        return "<td class=\"app-num\">"
             + Number(value).toFixed(1) + unit + "</td>";
    }

    function escapeHtml(text) {
        return String(text === null || text === undefined ? "" : text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    return { open: open, close: close };
}());

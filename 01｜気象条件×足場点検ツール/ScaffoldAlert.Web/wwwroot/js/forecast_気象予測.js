// ============================================================
//  気象予測の画面の動き
// ============================================================
//  ・区域を警戒度で塗り分けた地図
//  ・2つの表の絞り込み
//  ・行を押すと、その現場の時間ごとの動きが開く
// ============================================================

(function () {
    "use strict";

    // 行き先の頭に付ける文字。
    // ふだんは空。デモ画面から開かれたときだけ "/Demo" が入る。
    // これで、本物とデモで同じ動きを使い回せる。
    var 頭 = window.APP_PREFIX || "";


    var holder = document.getElementById("forecast-map-data");
    if (!holder) {
        return;
    }

    var data;
    try {
        data = JSON.parse(holder.textContent);
    } catch (e) {
        return;
    }

    // ============================================================
    //  地図
    // ============================================================
    // こちらは区域を塗るので、日本全体が入る位置と縮尺に固定する。
    // 縮尺の下限は 5。地理院タイルはズーム5〜16でしか配信されていない。
    var map = L.map("forecast-map", { minZoom: 5 });

    // 先に表示位置を決める。決まるまでタイルを取りに行かないため。
    map.setView([37.5, 137.0], 5);

    L.tileLayer(data.tileUrl, {
        attribution: data.attribution,

        // 配信されている範囲に合わせる。
        minZoom: 5,
        maxNativeZoom: 16,
        maxZoom: 18
    }).addTo(map);

    // 区域コード → 警戒度・風の予報
    var levelOf = {};
    (data.areas || []).forEach(function (a) {
        levelOf[a.code] = a;
    });

    // 区域の形は数MBある。画面の他の部分を先に使えるようにするため、
    // 後から読み込んで、届いたら地図に足す。
    fetch(頭 + "/Forecast/Areas")
        .then(function (res) { return res.json(); })
        .then(function (geo) {
            L.geoJSON(geo, {
                style: styleOf,
                onEachFeature: bindTooltip
            }).addTo(map);

            // 区域を塗ったあとに現場の丸を置く。
            // 先に置くと、区域の塗りに隠れてしまう。
            drawSites();
        })
        .catch(function () {
            // 区域の形が取れなくても、下の表で確認できる。
            // 地図だけを諦めて、現場の丸は置く。
            drawSites();
        });

    // ============================================================
    //  現場の丸
    // ============================================================
    //  部署の色で塗る。ダッシュボードと同じ色にして、
    //  2つの画面で同じ現場が同じ色に見えるようにしている。
    //
    //  「高」「中」の区域にある現場は大きく、縁を濃くする。
    //  色だけでは部署しか分からないので、
    //  危ない現場が一目で分かるようにするため。
    // 作った丸を覚えておく。部署で絞るときに出し入れするため。
    var siteMarkers = [];

    function drawSites() {
        (data.sites || []).forEach(function (site) {
            var marker = L.circleMarker([site.lat, site.lon],
                                        siteStyle(site));

            marker.bindTooltip(
                escapeHtml(site.name) + "<br>" +
                escapeHtml(site.dept) + "<br>" +
                escapeHtml(data.kind) + " "
                + escapeHtml(site.level || "なし"));

            marker.on("click", function () {
                openDetail(site.id);
            });

            siteMarkers.push({
                marker: marker,
                dept: site.dept,
                site: site
            });
        });

        applyDeptFilter();
    }

    // --- 部署で絞る ---
    // 選んだ部署の現場だけを地図に出す。
    // 隠すのではなく地図から外している。隠すだけだと
    // マウスを乗せたときの吹き出しが出てしまうため。
    function applyDeptFilter() {
        var box = document.getElementById("map-dept");
        var riskBox = document.getElementById("map-risk-only");
        var chosen = box ? box.value : "";
        var riskOnly = riskBox ? riskBox.checked : false;
        var shown = 0;

        siteMarkers.forEach(function (item) {
            var ok = (chosen === "" || item.dept === chosen);

            // 色が付いた区域の現場だけに絞る。
            // 何をもって「色が付いている」とするかは、
            // 地図の塗り分けと同じ決まりにする。
            if (ok && riskOnly) {
                ok = data.kind === "風"
                    ? item.site.windRank > 0
                    : item.site.level !== "";
            }

            if (ok) {
                item.marker.addTo(map);

                // 区域の塗りより前に出す。
                // 後から足しても、塗りに隠れることがあるため。
                item.marker.bringToFront();
                shown += 1;
            } else {
                map.removeLayer(item.marker);
            }
        });

        // ★言葉を分けている
        //   地図 … 現場の数
        //   表   … 行の数（現場×種類など、表によって数え方が違う）
        //   同じ「◯件を表示中」だと、数が合わないときに
        //   不具合だと受け取られてしまう。
        var label = document.getElementById("map-dept-count");
        if (label) {
            label.textContent = "地図に " + shown + " 件";
        }
    }

    var deptBox = document.getElementById("map-dept");
    if (deptBox) {
        deptBox.addEventListener("change", applyDeptFilter);
    }

    var riskBox = document.getElementById("map-risk-only");
    if (riskBox) {
        riskBox.addEventListener("change", applyDeptFilter);
    }

    // 地図の絞り込みを一度に戻す。
    // 部署とチェックの両方を戻す。
    var mapClear = document.getElementById("map-clear");
    if (mapClear) {
        mapClear.addEventListener("click", function () {
            if (deptBox) {
                deptBox.value = "";
            }
            if (riskBox) {
                riskBox.checked = false;
            }
            applyDeptFilter();
        });
    }

    // --- 凡例 ---
    // 風のときは、高/中ではなく風の強さの凡例を出す。
    // 塗り分けの根拠が変わるので、凡例も合わせないと読めない。
    (function drawLegend() {
        var box = document.getElementById("fc-legend");
        if (!box || data.kind !== "風") {
            return;
        }

        var html = "";

        (data.windLevels || []).slice().reverse().forEach(function (l) {
            html += "<span><span class=\"fc-swatch\" style=\"background:"
                  + l.color + "\"></span> "
                  + escapeHtml(l.name)
                  + "（" + escapeHtml(l.caution) + "）</span>";
        });

        html += "<span><span class=\"fc-swatch\" style=\"background:"
              + data.windNoneColor + "\"></span> 風の記載なし</span>"
              + "<span class=\"dash-hint\">"
              + "気象庁の予報用語による。やや強い風=10〜15m/s、"
              + "強い風=15〜20、非常に強い風=20〜30、猛烈な風=30以上。"
              + "強風の点検基準は10m/s。</span>";

        box.innerHTML = html;
    }());

    function siteStyle(site) {
        var fill = data.departmentColor[site.dept] || data.otherColor;

        // ★丸の大きさは、どれも同じにしている
        //
        //   以前は「色が付いた区域の現場」を大きくしていたが、
        //   「色が付いた区域の現場だけ」で絞れるようになったので
        //   その役目は終わった。
        //
        //   大きさが3種類あると
        //     ・地図が粒立って見え、現場の位置が読み取りにくい
        //     ・「大きい＝危ない」と「色＝部署」を同時に読むことになる
        //   見分ける手だては1つに絞るほうが読みやすい。
        //
        //   この地図は日本全体が入る縮尺なので、
        //   ダッシュボードの地図より大きくしないと見えない。
        var radius = 7;                 /* ★丸の大きさ */

        return {
            radius: radius,

            // 区域の色の上に置くので、白い縁で囲って浮かせる。
            // 縁が無いと、塗りに溶けて見えなくなる。
            color: "#ffffff",
            weight: 2,                  /* ★縁の太さ */
            opacity: 1,

            fill: true,
            fillOpacity: 1,
            fillColor: fill
        };
    }

    // --- 区域の塗り方 ---
    function styleOf(feature) {
        var code = String(feature.properties.code);
        var area = levelOf[code];

        // ★風のときは、風の強さで塗る。
        //   風の「高」「中」はめったに発表されないため、
        //   それだけで塗ると地図がほぼ灰色のままになる。
        //   気象庁の予報用語（やや強い風＝10〜15m/s など）から
        //   強さを見分けて色を分ける。
        if (data.kind === "風") {
            var rank = area ? (area.windRank || 0) : 0;

            return {
                fillColor: area && rank > 0
                    ? area.windColor : data.windNoneColor,
                color: "#5b6770",
                weight: 0.6,
                fillOpacity: rank > 0 ? 0.75 : 0.25
            };
        }

        var level = area ? area.level : "";

        return {
            fillColor: data.color[level] || data.noLevelColor,
            color: "#5b6770",
            weight: 0.6,
            // 発表がある区域を濃く塗って目立たせる。
            fillOpacity: level ? 0.75 : 0.25
        };
    }

    // --- 区域に乗せたときの吹き出し ---
    function bindTooltip(feature, layer) {
        var code = String(feature.properties.code);
        var area = levelOf[code];

        // 区域名は表と同じものを使う。地図の中に入っている名前は
        // 「東京都」のような別表記で、表と違って見えるため。
        var name = (area && area.name)
            ? area.name
            : (feature.properties.name || code);

        var level = (area && area.level) ? area.level : "なし";

        // 風の数値予測は気象庁から発表されていないため、
        // 府県天気予報の文章を手掛かりとして併せて出す。
        var wind = "";
        if (area && area.wind) {
            wind = "<br>風の予報: " + escapeHtml(area.wind);

            // 強さが分かるときは、階級と扱い、いつの分かも添える。
            if (area.windRank > 0) {
                wind += "<br>→ " + escapeHtml(area.windName)
                      + "（" + escapeHtml(area.windCaution) + "）"
                      + (area.windAt ? " " + escapeHtml(area.windAt) : "");
            }
        }

        layer.bindTooltip(escapeHtml(name) + "／"
                          + escapeHtml(data.kind) + " "
                          + escapeHtml(level) + wind);
    }

    // ============================================================
    //  表の絞り込み
    // ============================================================
    // 見出しの名前が違うだけで中身は同じなので、1つの作りで両方に使う。
    function setupFilter(tableId, countId, boxes) {
        var table = document.getElementById(tableId);
        if (!table) {
            return null;
        }

        var rows = table.querySelectorAll("tbody tr");
        var countLabel = document.getElementById(countId);

        function apply() {
            var shown = 0;

            Array.prototype.forEach.call(rows, function (row) {
                var ok = true;

                boxes.forEach(function (box) {
                    var chosen = box.element.value;
                    if (chosen !== "" && row.dataset[box.key] !== chosen) {
                        ok = false;
                    }
                });

                row.style.display = ok ? "" : "none";
                if (ok) {
                    shown += 1;
                }
            });

            if (countLabel) {
                countLabel.textContent = shown + " 件を表示中";
            }
        }

        boxes.forEach(function (box) {
            if (box.element) {
                box.element.addEventListener("change", apply);
            }
        });

        apply();

        // 絞り込みを戻すボタンから呼べるように、
        // 絞り込みの動きそのものを一緒に返す。
        rows.apply = apply;
        return rows;
    }

    function box(id, key) {
        return { element: document.getElementById(id), key: key };
    }

    // --- 絞り込みを一度に戻す ---
    // 欄を1つずつ「すべて」に戻すのは手間で、
    // 戻し忘れたまま「件数が合わない」と悩むことになる。
    //
    // 表ごとに絞り込みが別なので、ボタンも表ごとに置く。
    // 1つのボタンで全部戻すと、直したかった表以外まで戻ってしまう。
    function setupClear(buttonId, boxes, apply) {
        var button = document.getElementById(buttonId);
        if (!button) {
            return;
        }

        button.addEventListener("click", function () {
            boxes.forEach(function (b) {
                if (b.element) {
                    b.element.value = "";
                }
            });

            if (apply) {
                apply();
            }
        });
    }

    // --- 基準に達する予測 ---
    var overBoxes = [
        box("over-dept", "dept"),
        box("over-area", "area"),
        box("over-item", "item")
    ].filter(function (b) { return b.element !== null; });

    var overRows = setupFilter("over-table", "over-count", overBoxes);
    setupClear("over-clear", overBoxes, overRows ? overRows.apply : null);

    // --- 風の予報 ---
    // 地図の色と同じ根拠の一覧。
    // 気象庁は風の「高」「中」をほとんど出さないので、
    // 警報級の可能性の表だけだと風が1件も出ない。
    var windBoxes = [
        box("wind-dept", "dept"),
        box("wind-level", "level"),
        box("wind-caution", "caution")
    ].filter(function (b) { return b.element !== null; });

    var windRows = setupFilter("wind-table", "wind-count", windBoxes);
    setupClear("wind-clear", windBoxes, windRows ? windRows.apply : null);

    // --- 警報級の可能性 ---
    var rankBoxes = [
        box("rank-dept", "dept"),
        box("rank-area", "area"),
        box("rank-kind", "kind"),
        box("rank-level", "level")
    ].filter(function (b) { return b.element !== null; });

    var rankRows = setupFilter("rank-table", "rank-count", rankBoxes);
    setupClear("rank-clear", rankBoxes, rankRows ? rankRows.apply : null);

    // ============================================================
    //  時間ごとの動き（モーダル）
    // ============================================================
    var modal = document.getElementById("forecast-detail");
    var title = document.getElementById("fc-detail-title");
    var body = document.getElementById("fc-detail-body");

    // いま開いている現場のID。読み込んでいる途中で別の現場を
    // 押されたときに、古い方の結果で上書きしないために使う。
    var openSiteId = null;

    function closeDetail() {
        if (modal) {
            modal.classList.add("hidden");
        }
        openSiteId = null;
    }

    if (modal) {
        document.getElementById("fc-detail-close")
                .addEventListener("click", closeDetail);

        modal.addEventListener("click", function (e) {
            if (e.target === modal) {
                closeDetail();
            }
        });

        document.addEventListener("keydown", function (e) {
            if (e.key === "Escape" && !modal.classList.contains("hidden")) {
                closeDetail();
            }
        });
    }

    // 一覧の行を押したときに、その現場へ地図を寄せる。
    //
    // ★数字が大きいほど拡大する。1つ増えると倍の大きさ。
    //     16 … 建物が見える。周りが見えず、位置関係が分からない
    //     14 … 既定。町の様子と周りの現場が一緒に見える
    //     12 … 市全体
    var 寄る強さ = 14;                              /* ★寄る強さ */

    function focusSite(siteId) {
        if (!map) {
            return;
        }

        // 地図に出ている丸の中から、その現場を探す。
        // 部署で絞っているときは地図から外れていることがあるので、
        // 見つからなければ何もしない。
        for (var i = 0; i < siteMarkers.length; i++) {
            if (siteMarkers[i].site.id === siteId) {
                var site = siteMarkers[i].site;
                map.setView([site.lat, site.lon], 寄る強さ);
                return;
            }
        }
    }

    // 風の表の行を押したら、その現場へ地図を寄せる。
    // 警報級の可能性の表と同じ動きにする。
    if (windRows) {
        Array.prototype.forEach.call(windRows, function (row) {
            row.addEventListener("click", function () {
                Array.prototype.forEach.call(windRows, function (other) {
                    other.classList.remove("dash-row-selected");
                });
                row.classList.add("dash-row-selected");

                var siteId = Number(row.dataset.siteId);
                focusSite(siteId);
                openDetail(siteId);
            });
        });
    }

    if (rankRows) {
        Array.prototype.forEach.call(rankRows, function (row) {
            row.addEventListener("click", function () {
                Array.prototype.forEach.call(rankRows, function (other) {
                    other.classList.remove("dash-row-selected");
                });
                row.classList.add("dash-row-selected");

                var siteId = Number(row.dataset.siteId);

                // 地図も一緒に動かす。
                // 表と地図が左右に並んでいるので、
                // 「この現場はどこか」をその場で確かめられる。
                focusSite(siteId);

                openDetail(siteId);
            });
        });
    }

    function openDetail(siteId) {
        if (!modal) {
            return;
        }

        openSiteId = siteId;
        title.textContent = "読み込んでいます...";
        body.innerHTML = "";
        modal.classList.remove("hidden");

        // 見ている種類も渡す。その種類のことだけを出してもらう。
        // 雨を見ているときに風の話が混ざると、
        // 何の判断をしているのか分からなくなる。
        fetch(頭 + "/Forecast/SiteDetail?siteId="
              + encodeURIComponent(siteId)
              + "&kind=" + encodeURIComponent(data.kind))
            .then(function (res) { return res.json(); })
            .then(function (result) {
                if (openSiteId !== siteId) {
                    return;
                }
                renderDetail(result);
            })
            .catch(function (e) {
                title.textContent = "時間ごとの動き";
                body.innerHTML = "<p class=\"detail-error\">"
                    + "読み込めませんでした: " + escapeHtml(e.message) + "</p>";
            });
    }

    function renderDetail(result) {
        if (!result.found) {
            title.textContent = "時間ごとの動き";
            body.innerHTML = "<p class=\"detail-empty\">"
                + "この現場の予測はありません。</p>";
            return;
        }

        title.textContent = result.name;

        // --- 現場そのものの情報 ---
        var html = "<dl class=\"detail-list\">"
                 + "<dt>部署</dt><dd>" + escapeHtml(result.dept) + "</dd>"
                 + "<dt>住所</dt><dd>" + escapeHtml(result.addr) + "</dd>"
                 + "<dt>現場所長</dt><dd>"
                 + escapeHtml(result.manager) + "</dd>"
                 + "<dt>区域</dt><dd>" + escapeHtml(result.area) + "</dd>"
                 + "</dl>"
                 + "<p class=\"detail-note\">"
                 + "同じ区域の現場は同じ予測になります。"
                 + "気象庁の予測は区域単位で発表されるためです。"
                 + "（現在 " + escapeHtml(result.now) + "）</p>";

        // 風を見ているときは、地図を塗った根拠をそのまま出す。
        // これが無いと、地図に色が付いているのにモーダルは
        // 「発表はありません」となり、危ないのか分からなくなる。
        if (result.wind) {
            // まず「この先で最も強い風」。地図の色はこれで決まる。
            html += "<h5 class=\"detail-heading\">"
                  + "この先で最も強い風</h5>"
                  + "<table class=\"app-table detail-table\"><tbody>"
                  + "<tr><td>強さ</td><td>"
                  + "<span class=\"fc-swatch\" style=\"background:"
                  + result.wind.topColor + "\"></span> "
                  + escapeHtml(result.wind.topName)
                  + "（" + escapeHtml(result.wind.topCaution) + "）"
                  + "</td></tr>"
                  + "<tr><td>いつ</td><td>"
                  + escapeHtml(result.wind.topAt) + "</td></tr>";

            if (result.wind.topNote) {
                html += "<tr><td>点検基準との関係</td><td>"
                      + escapeHtml(result.wind.topNote) + "</td></tr>";
            }

            html += "</tbody></table>"
                  + "<p class=\"detail-note\">"
                  + "地図の色は、この強さで塗っています。</p>";

            // 次に、日ごとの予報。
            html += "<h5 class=\"detail-heading\">風の予報</h5>"
                  + "<table class=\"app-table detail-table\"><thead><tr>"
                  + "<th>いつ</th><th>予報</th><th>強さ</th>"
                  + "</tr></thead><tbody>";

            result.wind.days.forEach(function (d) {
                html += "<tr><td>" + escapeHtml(d.at) + "</td>"
                      + "<td>" + escapeHtml(d.text) + "</td>"
                      + "<td>"
                      + "<span class=\"fc-swatch\" style=\"background:"
                      + d.color + "\"></span> "
                      + escapeHtml(d.name)
                      + (d.rank > 0
                         ? "（" + escapeHtml(d.caution) + "）" : "")
                      + "</td></tr>";
            });

            html += "</tbody></table>"
                  + "<p class=\"detail-note\">"
                  + "気象庁の予報用語による風速の範囲です。"
                  + "やや強い風=10〜15m/s、強い風=15〜20、"
                  + "非常に強い風=20〜30、猛烈な風=30以上。"
                  + "強風の点検基準は10m/s。</p>";
        }

        // --- 種類ごとの、最も高くなるところ ---
        // まず「いつ、どれくらい危ないか」を一目で見せる。
        // 時間ごとの細かい動きは、その下に出す。
        if (result.summary && result.summary.length > 0) {
            html += "<h5 class=\"detail-heading\">警報級の可能性</h5>"
                  + "<table class=\"app-table detail-table\"><thead><tr>"
                  + "<th>種類</th><th>可能性</th>"
                  + "<th>最も高くなる時間帯</th>"
                  + "<th>意味</th>"
                  + "</tr></thead><tbody>";

            result.summary.forEach(function (s) {
                html += "<tr" + (s.isNow ? " class=\"fc-now\"" : "") + ">"
                      + "<td>" + escapeHtml(s.kind) + "</td>"
                      + "<td>" + escapeHtml(s.level) + "</td>"
                      + "<td>" + escapeHtml(s.at)
                      + (s.isNow ? " <strong>（いま）</strong>" : "") + "</td>"
                      + "<td>" + escapeHtml(s.note || "") + "</td></tr>";
            });

            html += "</tbody></table>";
        }

        if (result.kinds.length === 0) {
            // 風のときは、上に風の強さを出してある。
            // ここで「発表はありません」だけを出すと、
            // 危ないのかどうか分からなくなる。
            html += "<p class=\"detail-empty\">"
                  + "気象庁からの【高】【中】の発表は、"
                  + escapeHtml(data.kind)
                  + "についてはありません。"
                  + (result.wind
                     ? "上の風の強さで地図の色を付けています。"
                     : "")
                  + "</p>";
            body.innerHTML = html;
            return;
        }

        html += "<h5 class=\"detail-heading\">"
              + escapeHtml(data.kind) + "の時間ごとの動き</h5>";

        result.kinds.forEach(function (k) {
            html += "<h5 class=\"detail-heading\">"
                  + escapeHtml(k.kind) + "</h5>"
                  + "<table class=\"app-table detail-table\"><thead><tr>"
                  + "<th>対象日時</th><th>可能性</th><th>意味</th>";

            html += "</tr></thead><tbody>";

            k.times.forEach(function (t) {
                html += "<tr" + (t.now ? " class=\"fc-now\"" : "") + ">"
                      + "<td>" + escapeHtml(t.at)
                      + (t.now ? " <strong>（いま）</strong>" : "") + "</td>"
                      + "<td>" + escapeHtml(t.level || "なし") + "</td>";
                html += "<td>" + escapeHtml(t.note || "") + "</td>";
                html += "</tr>";
            });

            html += "</tbody></table>";
        });

        body.innerHTML = html;
    }

    // ============================================================
    //  小道具
    // ============================================================
    function escapeHtml(text) {
        return String(text === null || text === undefined ? "" : text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }
}());

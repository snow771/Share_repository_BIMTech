// ============================================================
//  ダッシュボードの動き
// ============================================================
//  ・地図に現場の丸を並べる
//  ・色分けを「アラート状況」と「部署」で切り替える
//  ・アラートの一覧を絞り込む
//  ・一覧の行を押すと、その現場に地図が寄る
//
//  外部の部品は地図(Leaflet)だけ。それ以外は素のJavaScriptで書いている。
//  仕組みを追いやすくするため。
// ============================================================

(function () {
    "use strict";

    // 行き先の頭に付ける文字。
    // ふだんは空。デモ画面から開かれたときだけ "/Demo" が入る。
    // これで、本物とデモで同じ動きを使い回せる。
    var 頭 = window.APP_PREFIX || "";


    // --- 困ったことを画面に出す ---
    // 何も出ないまま灰色の四角が残ると、原因の見当が付かない。
    //
    // 何についてのお知らせかを覚えておく。
    // タイルが取れるようになったときに、タイルの話だけを消すため。
    // 他のお知らせまで一緒に消すと、伝わるはずのことが伝わらなくなる。
    var messageKind = null;

    function showMessage(kind, text) {
        var box = document.getElementById("map-message");
        if (box) {
            messageKind = kind;
            box.textContent = text;
            box.style.display = "block";
        }
    }

    function hideMessage(kind) {
        var box = document.getElementById("map-message");
        if (box && messageKind === kind) {
            messageKind = null;
            box.textContent = "";
            box.style.display = "none";
        }
    }

    // --- 画面から中身を読み取る ---
    var holder = document.getElementById("map-data");
    if (!holder) {
        return;
    }

    var data;
    try {
        data = JSON.parse(holder.textContent);
    } catch (e) {
        showMessage("data", "地図に渡す情報を読み取れませんでした: " + e.message);
        return;
    }

    // ============================================================
    //  地図を作る
    // ============================================================
    // 縮尺の下限は 5。
    //
    // 地理院タイル(淡色地図)は、ズーム5〜16でしか配信されていない。
    // 4まで引くとタイルが1枚も無く、地図が灰色のままになる。
    // 5は日本全体が入る大きさなので、実用上これで足りる。
    var map = L.map("site-map", { minZoom: 5 });

    // 先に表示位置を決めてしまう。日本全体が入る位置と縮尺。
    //
    // Leaflet は表示位置が決まるまでタイルを取りに行かない。
    // 位置を決めるのを後回しにすると、その手前で何かあったときに
    // 「灰色の四角に拡大縮小のボタンだけ」という状態になり、
    // 背景地図が来ていないのか処理が止まったのか区別が付かない。
    map.setView([37.5, 137.0], 5);

    // 背景地図。地理院タイルだけを読み込む。
    // 出典表示は地理院タイルの利用条件のため必須。
    var tiles = L.tileLayer(data.tileUrl, {
        attribution: data.attribution,

        // 配信されている範囲。ここを外すと、無いタイルを
        // 取りに行って「読み込めません」と出てしまう。
        minZoom: 5,
        maxNativeZoom: 16,

        // 16より寄せたいときは、16のタイルを引き伸ばして使う。
        // 取りに行かないので、失敗にはならない。
        maxZoom: 18
    });

    // タイルが取れないときに気づけるようにする。
    // 1枚の失敗は端の欠けなどでも起こるので、続けて失敗したときだけ出す。
    var tileErrors = 0;
    tiles.on("tileerror", function () {
        tileErrors += 1;
        if (tileErrors === 5) {
            showMessage("tile",
                "背景地図（地理院タイル）を読み込めません。"
                + "このPCから https://cyberjapandata.gsi.go.jp へ"
                + "出られるかを確認してください。"
                + "現場の丸と、一覧からの絞り込みは使えます。");
        }
    });

    // タイルが1枚でも取れたら、タイルについてのお知らせは消す。
    // 画面の大きさが決まる前に一度失敗しても、そのあと取れれば
    // 困っていないので、出したままにしない。
    tiles.on("tileload", function () {
        tileErrors = 0;
        hideMessage("tile");
    });

    tiles.addTo(map);

    // --- 丸の色と大きさを決める ---
    // 色分けの切り替えでここだけが変わる。
    function styleOf(site, mode, focused) {
        var fill;
        var radius;

        if (mode === "dept") {
            fill = data.departmentColor[site.dept] || data.otherColor;
            radius = 5;
        } else {
            fill = data.alertColor[site.alert] || data.otherColor;
            // アラートが無い現場は小さくして、出ている現場を目立たせる。
            radius = site.alert === "なし" ? 3 : 7;
        }

        return {
            radius: focused ? 10 : radius,
            color: focused ? "#0b0b0b" : "#ffffff",
            weight: focused ? 3 : 1,
            fill: true,
            fillOpacity: 0.9,
            fillColor: fill
        };
    }

    // --- 現場ごとの丸を作る ---
    var markers = {};      // 現場ID → 丸
    var colorMode = "alert";
    var focusId = null;

    // いま開いている吹き出しの丸。開いていなければ空。
    //
    // 丸や一覧の行を押すと、こちらから吹き出しを開いている。
    // このとき地図も動くので、丸がマウスの下から離れてしまい、
    // Leaflet は「マウスが外れた」と判断できない。
    // その結果、吹き出しが出たままになる。
    // 開いたものを1つだけ覚えておき、自分で閉じる。
    var openedMarker = null;

    function closeTooltip() {
        if (openedMarker !== null) {
            openedMarker.closeTooltip();
            openedMarker = null;
        }
    }

    // 地図の何もないところを押したら閉じる。
    // 丸を押したときは、この合図は上がってこない
    // (Leaflet が丸の側で止めるため)。
    map.on("click", closeTooltip);

    (data.sites || []).forEach(function (site) {
        var marker = L.circleMarker([site.lat, site.lon],
                                    styleOf(site, colorMode, false));

        // 丸に乗せると現場の情報が出る。
        // 色だけでは部署を見分けられないため、必ず文字でも出す。
        marker.bindTooltip(
            escapeHtml(site.name) + "<br>" +
            escapeHtml(site.dept) + "<br>" +
            escapeHtml(site.addr) + "<br>" +
            "対応状況: " + escapeHtml(site.alert));

        marker.on("click", function () {
            focusSite(site.id, true);
            openDetail(site.id);
        });

        // マウスが離れたら閉じる。
        // ふだんの「乗せると出る」動きは Leaflet が面倒を見るが、
        // こちらから開いたものは覚えている印も消しておく。
        marker.on("mouseout", function () {
            if (openedMarker === marker) {
                openedMarker = null;
            }
        });

        marker.addTo(map);
        markers[site.id] = { marker: marker, site: site };
    });

    // --- 最初は全現場が収まるように表示する ---
    function showAll() {
        closeTooltip();
        focusId = null;
        redraw();

        if (data.sites.length === 1) {
            map.setView([data.sites[0].lat, data.sites[0].lon], 14);
            return;
        }

        var bounds = L.latLngBounds(data.sites.map(function (s) {
            return [s.lat, s.lon];
        }));

        // 現場が収まるところまで寄せる。
        // 余白を小さくしているのは、日本の外が映り込む分を減らすため。
        map.fitBounds(bounds, { padding: [8, 8] });
    }

    // --- 色や大きさを塗り直す ---
    function redraw() {
        Object.keys(markers).forEach(function (id) {
            var item = markers[id];
            var focused = (focusId !== null && String(focusId) === String(id));
            item.marker.setStyle(styleOf(item.site, colorMode, focused));
        });
        drawLegend();
    }

    // --- 選んだ現場に寄る ---
    function focusSite(siteId, keepZoom) {
        // 前に開いた吹き出しを先に閉じる。
        // 閉じずに次を開くと、押した数だけ出たままになる。
        closeTooltip();

        focusId = siteId;
        redraw();

        var item = markers[siteId];
        if (!item) {
            return;
        }

        // 一覧の行を押したときに、どこまで寄るか。
        //
        // ★数字が大きいほど拡大する。1つ増えると倍の大きさ。
        //     16 … 建物が見える。周りが見えず、位置関係が分からない
        //     14 … 既定。町の様子と周りの現場が一緒に見える
        //     12 … 市全体
        //
        // 地図の丸を押したときは、いまの縮尺のままにする。
        // 勝手に拡大すると、周りの現場が見えなくなって戻れなくなる。
        var zoom = keepZoom ? map.getZoom() : 14;   /* ★寄る強さ */
        map.setView([item.site.lat, item.site.lon], zoom);

        item.marker.openTooltip();
        openedMarker = item.marker;
    }

    // ============================================================
    //  凡例
    // ============================================================
    var legend = L.control({ position: "bottomright" });

    legend.onAdd = function () {
        var box = L.DomUtil.create("div", "dash-legend");
        // 凡例の上でドラッグしても地図が動かないようにする。
        L.DomEvent.disableClickPropagation(box);
        return box;
    };

    legend.addTo(map);

    function drawLegend() {
        var box = legend.getContainer();
        var title;
        var items = [];

        if (colorMode === "dept") {
            title = "部署";

            // 表に載っている部署を、決めた順に並べる。
            Object.keys(data.departmentColor).forEach(function (name) {
                items.push({
                    name: name,
                    color: data.departmentColor[name],
                    count: countSites(function (s) { return s.dept === name; })
                });
            });

            // 表にない部署はまとめて「その他」にする。
            var other = countSites(function (s) {
                return !data.departmentColor[s.dept];
            });
            if (other > 0) {
                items.push({
                    name: "その他", color: data.otherColor, count: other
                });
            }
        } else {
            title = "対応状況";
            Object.keys(data.alertColor).forEach(function (name) {
                items.push({
                    name: name,
                    color: data.alertColor[name],
                    count: countSites(function (s) { return s.alert === name; })
                });
            });
        }

        var html = "<div class=\"dash-legend-title\">" + title + "</div>";
        items.forEach(function (item) {
            if (item.count === 0) {
                return;
            }
            html += "<div class=\"dash-legend-row\">"
                  + "<span class=\"dash-legend-dot\" style=\"background:"
                  + item.color + "\"></span>"
                  + "<span class=\"dash-legend-name\">"
                  + escapeHtml(item.name) + "</span>"
                  + "<span class=\"dash-legend-count\">"
                  + item.count + "</span></div>";
        });

        box.innerHTML = html;
    }

    function countSites(test) {
        var n = 0;
        data.sites.forEach(function (s) {
            if (test(s)) {
                n += 1;
            }
        });
        return n;
    }

    // ============================================================
    //  色分けの切り替え
    // ============================================================
    var radios = document.querySelectorAll("input[name=\"color-mode\"]");
    Array.prototype.forEach.call(radios, function (radio) {
        radio.addEventListener("change", function () {
            colorMode = radio.value;
            redraw();
        });
    });

    var resetButton = document.getElementById("map-reset");
    if (resetButton) {
        resetButton.addEventListener("click", showAll);
    }

    // ============================================================
    //  アラートの一覧
    // ============================================================
    var table = document.getElementById("alert-table");

    if (table) {
        var rows = table.querySelectorAll("tbody tr");
        var deptBox = document.getElementById("filter-dept");
        var eventBox = document.getElementById("filter-event");
        var statusBox = document.getElementById("filter-status");
        var keywordBox = document.getElementById("filter-keyword");
        var countLabel = document.getElementById("alert-count");

        // --- 絞り込み ---
        //
        // ★地図も一緒に絞る
        //   一覧で「大阪建設だけ」に絞ったのに、地図は全部のまま、
        //   では見比べられない。絞り込んだら地図も同じ中身にする。
        //
        //   ただし、何も絞っていないときは全現場を出す。
        //   アラートが出ている現場は数件しかないので、
        //   最初からその数件だけの地図になると、
        //   会社全体のどこに現場があるのかが分からなくなる。
        function applyFilter() {
            var dept = deptBox.value;
            var event = eventBox.value;
            var status = statusBox.value;
            var keyword = keywordBox.value.trim().toLowerCase();
            var shown = 0;

            // 何か1つでも絞り込んでいるか。
            var 絞っている = (dept !== "" || event !== ""
                              || status !== "" || keyword !== "");

            // 絞り込みに残った現場の番号を覚えておく。地図で使う。
            var 残った = {};

            Array.prototype.forEach.call(rows, function (row) {
                var ok =
                    (dept === "" || row.dataset.dept === dept) &&
                    (event === "" || row.dataset.event === event) &&
                    (status === "" || row.dataset.status === status) &&
                    (keyword === ""
                     || row.dataset.search.toLowerCase().indexOf(keyword) >= 0);

                row.style.display = ok ? "" : "none";
                if (ok) {
                    shown += 1;
                    残った[row.dataset.siteId] = true;
                }
            });

            countLabel.textContent = shown + " 件を表示中";

            applyMapFilter(絞っている ? 残った : null);
        }

        // --- 地図を一覧に合わせる ---
        // 渡された番号の現場だけを地図に出す。
        // null のときは全部出す（絞り込んでいないとき）。
        //
        // 隠すのではなく地図から外している。隠すだけだと
        // マウスを乗せたときの吹き出しが出てしまうため。
        function applyMapFilter(残った) {
            var note = document.getElementById("map-filter-note");
            var 出した = 0;

            Object.keys(markers).forEach(function (id) {
                var item = markers[id];
                var ok = (残った === null) || (残った[id] === true);

                if (ok) {
                    item.marker.addTo(map);
                    出した += 1;
                } else {
                    map.removeLayer(item.marker);
                }
            });

            // 地図が絞られていることを、地図の上に必ず出す。
            // 断りが無いと「現場が消えた」と受け取られる。
            if (note) {
                if (残った === null) {
                    note.classList.add("hidden");
                } else {
                    note.textContent = "一覧の絞り込みに合わせて "
                                     + 出した + " 件だけを出しています";
                    note.classList.remove("hidden");
                }
            }
        }

        [deptBox, eventBox, statusBox].forEach(function (box) {
            box.addEventListener("change", applyFilter);
        });
        keywordBox.addEventListener("input", applyFilter);

        // --- 絞り込みを一度に戻す ---
        // 4つの欄を1つずつ戻すのは手間で、戻し忘れたまま
        // 「件数が合わない」と悩むことになる。
        var clearButton = document.getElementById("filter-clear");
        if (clearButton) {
            clearButton.addEventListener("click", function () {
                deptBox.value = "";
                eventBox.value = "";
                statusBox.value = "";
                keywordBox.value = "";

                applyFilter();

                // 選んでいた現場も外して、地図を全体に戻す。
                // 絞り込みだけ消えて1件に寄ったままだと、
                // 戻った気がしない。
                var pick = document.getElementById("alert-pick");
                if (pick) {
                    pick.classList.add("hidden");
                }

                Array.prototype.forEach.call(rows, function (other) {
                    other.classList.remove("dash-row-selected");
                });

                showAll();
            });
        }

        // --- 行を押すと、その現場に地図が寄る ---
        Array.prototype.forEach.call(rows, function (row) {
            row.addEventListener("click", function () {
                Array.prototype.forEach.call(rows, function (other) {
                    other.classList.remove("dash-row-selected");
                });
                row.classList.add("dash-row-selected");

                // ここでは詳細を開かない。
                // 一覧で選ぶ → 地図がその現場に寄る → 丸を押す → 詳細、
                // という順にする。一覧を押しただけで画面が覆われると、
                // どの現場を見ているのか分からないまま詳細が出てしまう。
                //
                // 地図は右隣に見えているので、画面を送る必要は無い。
                var siteId = Number(row.dataset.siteId);
                if (markers[siteId]) {
                    focusSite(siteId, false);
                }

                showPick(row);
            });
        });

        // --- 選んだ1件について、対応へ進む道を出す ---
        var pick = document.getElementById("alert-pick");
        var pickName = document.getElementById("alert-pick-name");
        var pickLink = document.getElementById("alert-pick-link");

        function showPick(row) {
            if (!pick) {
                return;
            }
            pickName.textContent = row.dataset.name;
            pickLink.href = 頭 + "/Alert?id="
                          + encodeURIComponent(row.dataset.alertId);
            pick.classList.remove("hidden");
        }

        applyFilter();
    }

    // ============================================================
    //  共通の小道具
    // ============================================================
    // 現場の詳細を開く。
    // 詳細は別のファイル (siteDetail_現場の詳細.js) が持っている。
    // 読み込まれていない場合でも地図が止まらないよう、あるか確かめる。
    function openDetail(siteId) {
        if (window.SiteDetail) {
            window.SiteDetail.open(siteId);
        }
    }

    // 現場名や住所に < や & が入っていても画面が壊れないようにする。
    function escapeHtml(text) {
        return String(text === null || text === undefined ? "" : text)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    // --- 地図の大きさが変わったことを知らせる ---
    // 地図は2列の中に置いてあり、作った時点では置き場の大きさが
    // まだ決まっていないことがある。そのままだと灰色の帯になるので、
    // 決まってから作り直させる。
    var mapBox = document.getElementById("site-map");

    function refreshSize() {
        map.invalidateSize();
    }

    // 画面の大きさが変わったときも合わせる。
    window.addEventListener("resize", refreshSize);

    // 置き場の大きさが決まった(または変わった)ことを見届けて、
    // そのときに全体表示を組み立て直す。
    //
    // 起動した直後は、まだ大きさが決まっていない。その状態で
    // 全体表示を計算すると位置も縮尺も定まらず、タイルを
    // 取りに行けないまま灰色で止まる。
    // 「全体を表示」を押すと出るのは、そのときには決まっているため。
    var sizedOnce = false;

    function onSized() {
        if (mapBox.clientWidth === 0 || mapBox.clientHeight === 0) {
            return;
        }

        refreshSize();

        // 最初に大きさが決まったときだけ、全体表示に戻す。
        // 毎回やると、利用者が寄せた場所が勝手に戻ってしまう。
        if (!sizedOnce) {
            sizedOnce = true;
            showAll();
        }
    }

    if (window.ResizeObserver) {
        new ResizeObserver(onSized).observe(mapBox);
    } else {
        // 古いブラウザ向けの控え。読み込みが終わってから合わせる。
        window.addEventListener("load", onSized);
        window.setTimeout(onSized, 300);
    }

    // --- 最初の表示 ---
    // ここまでで何かあっても、地図そのものは出たままにする。
    // 止まったことを黙って隠すと、原因の切り分けができない。
    try {
        showAll();
    } catch (e) {
        showMessage("view", "地図の表示位置を決められませんでした: " + e.message);
    }
}());

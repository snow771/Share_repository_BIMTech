// ============================================================
//  アラート対応のタブ
// ============================================================
//  3つの入力を1つの画面に並べると縦に長くなり、
//  どれを使えばよいのか分かりにくい。
//  タブで1つずつ出す。
// ============================================================

(function () {
    "use strict";

    var tabs = document.getElementById("alert-tabs");
    if (!tabs) {
        return;
    }

    var buttons = tabs.querySelectorAll(".tab-button");
    var panels = document.querySelectorAll(".tab-panel");

    function show(name) {
        Array.prototype.forEach.call(buttons, function (b) {
            b.classList.toggle("tab-button-on", b.dataset.tab === name);
        });

        Array.prototype.forEach.call(panels, function (p) {
            p.classList.toggle("hidden", p.dataset.tab !== name);
        });
    }

    Array.prototype.forEach.call(buttons, function (b) {
        b.addEventListener("click", function () {
            show(b.dataset.tab);
        });
    });

    // 入力し直しになったときは、そのときのタブを開いておく。
    // 先頭に戻ると、どこを直せばよいのか分からなくなる。
    show(tabs.dataset.open || "instruct");
}());

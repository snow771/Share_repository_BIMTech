// ============================================================
//  取得と判定のボタン
// ============================================================
//  実行は1〜2分かかる。押しても何も変わらないと、
//  効いていないと思って何度も押してしまう。
//  押したことが分かるようにし、二度押しも止める。
// ============================================================

(function () {
    "use strict";

    var running = document.getElementById("running");
    var buttons = [
        document.getElementById("run-all"),
        document.getElementById("run-fetch")
    ];

    buttons.forEach(function (button) {
        if (!button) {
            return;
        }

        button.form.addEventListener("submit", function () {
            // 押せないようにする。二重に動かすと、
            // 気象庁へ二度取りに行くことになる。
            buttons.forEach(function (b) {
                if (b) {
                    b.disabled = true;
                }
            });

            button.textContent = "実行しています...";

            if (running) {
                running.hidden = false;
            }
        });
    });
}());

// ============================================================
//  上のメニューの ＜参考＞ プルダウン
// ============================================================
//  見るだけの画面（判定確認・基準 / 取得情報確認）は
//  使う頻度が低いので、ここにまとめてある。
// ============================================================

(function () {
    "use strict";

    var button = document.getElementById("menu-more");
    var list = document.getElementById("menu-more-list");

    if (!button || !list) {
        return;
    }

    var box = button.parentElement;

    function open() {
        list.classList.remove("hidden");
        box.classList.add("open");
        button.setAttribute("aria-expanded", "true");
    }

    function close() {
        list.classList.add("hidden");
        box.classList.remove("open");
        button.setAttribute("aria-expanded", "false");
    }

    function isOpen() {
        return !list.classList.contains("hidden");
    }

    button.addEventListener("click", function (e) {
        // ここで止めておかないと、下の「外を押したら閉じる」が
        // すぐに動いてしまい、開いた瞬間に閉じてしまう。
        e.stopPropagation();

        if (isOpen()) {
            close();
        } else {
            open();
        }
    });

    // 一覧の中を押したときは閉じない。
    // 押した先へ移るので、そのままで問題ない。
    list.addEventListener("click", function (e) {
        e.stopPropagation();
    });

    // 画面のどこかを押したら閉じる。
    document.addEventListener("click", function () {
        if (isOpen()) {
            close();
        }
    });

    // Esc でも閉じられるようにする。
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && isOpen()) {
            close();
        }
    });
}());

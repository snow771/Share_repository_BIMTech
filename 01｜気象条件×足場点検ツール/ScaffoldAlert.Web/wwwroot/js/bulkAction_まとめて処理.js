// ============================================================
//  アラートをまとめて処理する
// ============================================================
//  左端のチェックで選ぶと、上にボタンが出る。
//
//  ・まとめて指示済にする … 同じ担当者が複数の現場を持つとき
//  ・まとめて対象外にする … 同じ理由で点検が不要な現場が並ぶとき
//
//  「表示中のすべてを選ぶ」は、絞り込みで残っている行だけを選ぶ。
//  部署や担当者で絞ってから押せば、その分だけがまとまる。
// ============================================================

(function () {
    "use strict";

    // 行き先の頭に付ける文字。
    // ふだんは空。デモ画面から開かれたときだけ "/Demo" が入る。
    // これで、本物とデモで同じ動きを使い回せる。
    var 頭 = window.APP_PREFIX || "";


    var table = document.getElementById("alert-table");
    if (!table) {
        return;
    }

    var boxes = table.querySelectorAll(".bulk-pick");
    var allBox = document.getElementById("bulk-all");
    var bar = document.getElementById("bulk-bar");
    var countLabel = document.getElementById("bulk-count");

    var modal = document.getElementById("bulk-modal");
    var form = document.getElementById("bulk-form");
    var idsBox = document.getElementById("bulk-ids");
    var title = document.getElementById("bulk-title");
    var target = document.getElementById("bulk-target");
    var warn = document.getElementById("bulk-warn");
    var whoLabel = document.getElementById("bulk-who-label");
    var noteLabel = document.getElementById("bulk-note-label");
    var whoInput = document.getElementById("bulk-who");
    var noteInput = document.getElementById("bulk-note");

    // ============================================================
    //  選ぶ
    // ============================================================

    // いま選ばれているものを数える。
    // 絞り込みで隠れている行は、チェックが付いていても数えない。
    // 見えていないものを一緒に処理すると、
    // 何を処理したのか分からなくなる。
    function picked() {
        var list = [];

        Array.prototype.forEach.call(boxes, function (box) {
            if (box.checked && isShown(box)) {
                list.push(box.value);
            }
        });

        return list;
    }

    // その行が、いま表に出ているか。
    function isShown(box) {
        var row = box.closest("tr");
        return row !== null && row.style.display !== "none";
    }

    function refresh() {
        var list = picked();

        countLabel.textContent = list.length;
        bar.classList.toggle("hidden", list.length === 0);

        // 「表示中のすべて」の状態を合わせる。
        var shown = 0;
        Array.prototype.forEach.call(boxes, function (box) {
            if (isShown(box)) {
                shown += 1;
            }
        });

        if (allBox) {
            allBox.checked = shown > 0 && list.length === shown;

            // 一部だけ選ばれているときは、中間の見た目にする。
            allBox.indeterminate = list.length > 0 && list.length < shown;
        }
    }

    Array.prototype.forEach.call(boxes, function (box) {
        // チェックを押したときに、行を選んだ扱いにしない。
        // 行を押すと地図が寄る動きがあるため、そちらを止める。
        box.addEventListener("click", function (e) {
            e.stopPropagation();
        });

        box.addEventListener("change", refresh);
    });

    if (allBox) {
        allBox.addEventListener("click", function (e) {
            e.stopPropagation();
        });

        allBox.addEventListener("change", function () {
            Array.prototype.forEach.call(boxes, function (box) {
                if (isShown(box)) {
                    box.checked = allBox.checked;
                }
            });
            refresh();
        });
    }

    // 絞り込みが変わったら、数え直す。
    // 隠れた行のチェックは残るが、数には入らない。
    ["filter-dept", "filter-event", "filter-status"].forEach(function (id) {
        var box = document.getElementById(id);
        if (box) {
            box.addEventListener("change", refresh);
        }
    });

    var keyword = document.getElementById("filter-keyword");
    if (keyword) {
        keyword.addEventListener("input", refresh);
    }

    // --- 選択を外す ---
    document.getElementById("bulk-clear")
            .addEventListener("click", function () {
        Array.prototype.forEach.call(boxes, function (box) {
            box.checked = false;
        });
        refresh();
    });

    // ============================================================
    //  まとめて処理する
    // ============================================================
    document.getElementById("bulk-instruct")
            .addEventListener("click", function () {
        openModal("instruct");
    });

    document.getElementById("bulk-skip")
            .addEventListener("click", function () {
        openModal("skip");
    });

    function openModal(kind) {
        var list = picked();
        if (list.length === 0) {
            return;
        }

        // 選んだIDを、送るための入れ物にする。
        idsBox.innerHTML = "";
        list.forEach(function (id) {
            var hidden = document.createElement("input");
            hidden.type = "hidden";
            hidden.name = "alertIds";
            hidden.value = id;
            idsBox.appendChild(hidden);
        });

        target.textContent = list.length + " 件の現場に、同じ内容を記録します。";

        if (kind === "skip") {
            form.action = 頭 + "/Alert/BulkNotRequired";
            title.textContent = "まとめて対象外にする";
            whoLabel.textContent = "判断者名（必須）";
            noteLabel.textContent = "対象外とする理由（必須）";
            noteInput.name = "why";
            noteInput.placeholder =
                "例: 現場リストが未更新で、いずれも休止中のため。";
            warn.classList.remove("hidden");
        } else {
            form.action = 頭 + "/Alert/BulkInstruct";
            title.textContent = "まとめて指示済にする";
            whoLabel.textContent = "指示者名（必須）";
            noteLabel.textContent = "指示内容メモ";
            noteInput.name = "memo";
            noteInput.placeholder =
                "例: 所長へ電話連絡。明朝の作業開始前に点検を実施するよう指示。";
            warn.classList.add("hidden");
        }

        modal.classList.remove("hidden");
        whoInput.focus();
    }

    function closeModal() {
        modal.classList.add("hidden");
    }

    document.getElementById("bulk-close")
            .addEventListener("click", closeModal);

    document.getElementById("bulk-cancel")
            .addEventListener("click", closeModal);

    modal.addEventListener("click", function (e) {
        if (e.target === modal) {
            closeModal();
        }
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape" && !modal.classList.contains("hidden")) {
            closeModal();
        }
    });

    // --- 送る前の確かめ ---
    form.addEventListener("submit", function (e) {
        if (whoInput.value.trim() === "") {
            e.preventDefault();
            alert("名前を入れてください。");
            whoInput.focus();
            return;
        }

        // 対象外は理由が必須。臨検で問われることがある。
        if (noteInput.name === "why" && noteInput.value.trim() === "") {
            e.preventDefault();
            alert("対象外とする理由を入れてください。");
            noteInput.focus();
            return;
        }

        // 二度押しで、同じ内容が二度記録されないようにする。
        document.getElementById("bulk-submit").disabled = true;
    });

    // --- 最初の状態 ---
    refresh();
}());

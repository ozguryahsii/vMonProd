/* vMon Motion FX — bağımlılıksız (Web Animations API) hareket katmanı.
   Sayfa girişi + kart stagger + modal/dropdown yaylanması.
   İlerletici iyileştirme: JS yoksa/tarayıcı desteklemiyorsa her şey normal görünür.
   prefers-reduced-motion'a saygı duyar. */
(function () {
    "use strict";
    var reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    var canAnimate = !!(Element.prototype.animate);

    function done() { document.documentElement.classList.add("motion-done"); }

    if (reduced || !canAnimate) { done(); return; }

    var EASE_OUT = "cubic-bezier(0.16, 1, 0.3, 1)";      /* easeOutExpo benzeri — premium his */
    var SPRING   = "cubic-bezier(0.34, 1.56, 0.64, 1)";  /* hafif taşmalı yay */

    function enterPage() {
        var main = document.querySelector("main.page-enter");
        if (!main) { done(); return; }

        /* Ana içerik: yumuşak yüksel + belir */
        main.animate(
            [{ opacity: 0, transform: "translateY(10px)" }, { opacity: 1, transform: "none" }],
            { duration: 380, easing: EASE_OUT, fill: "both" }
        ).onfinish = done;

        /* Kart/stat stagger — yalnız görünür ilk 24 öge (performans) */
        var items = main.querySelectorAll(".card, .stat, .status-card");
        var count = Math.min(items.length, 24);
        for (var i = 0; i < count; i++) {
            items[i].animate(
                [{ opacity: 0, transform: "translateY(12px) scale(.985)" }, { opacity: 1, transform: "none" }],
                { duration: 420, delay: 40 + i * 28, easing: EASE_OUT, fill: "backwards" }
            );
        }
        /* güvenlik: animasyon bitmese bile 900ms sonra görünür kabul et */
        setTimeout(done, 900);
    }

    /* Alpine dropdown'ları ve modallar için genel yay animasyonu.
       x-show ile açılan öğeler MutationObserver ile yakalanır. */
    function popIn(el) {
        try {
            el.animate(
                [{ opacity: 0, transform: "translateY(-6px) scale(.97)" }, { opacity: 1, transform: "none" }],
                { duration: 240, easing: SPRING, fill: "backwards" }
            );
        } catch (e) { /* yoksay */ }
    }
    function watchPops() {
        var mo = new MutationObserver(function (muts) {
            muts.forEach(function (m) {
                if (m.type !== "attributes") return;
                var el = m.target;
                /* x-show görünür oldu (style.display '' veya != none) */
                if (el.matches && el.matches("[x-show], .modal-overlay") && el.style.display !== "none" && !el.hidden) popIn(el);
            });
        });
        mo.observe(document.body, { attributes: true, attributeFilter: ["style", "hidden"], subtree: true });
    }

    /* Global mini yardımcı: diğer sayfa script'leri kullanabilsin */
    window.vMotion = {
        pop: popIn,
        rise: function (el, delay) {
            if (!el || !el.animate) return;
            el.animate(
                [{ opacity: 0, transform: "translateY(10px)" }, { opacity: 1, transform: "none" }],
                { duration: 360, delay: delay || 0, easing: EASE_OUT, fill: "backwards" }
            );
        }
    };

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", function () { enterPage(); watchPops(); });
    else { enterPage(); watchPops(); }
})();

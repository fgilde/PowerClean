/* Simple, dependency-free lightbox for PowerClean screenshots.
   Makes gallery / spotlight / hero / docs images clickable to view large,
   with prev/next navigation and keyboard support. */
(function () {
    var selector = ".gallery img, .spot-shot img, .hero-shots img, .docs-main img";
    var imgs = Array.prototype.slice.call(document.querySelectorAll(selector));
    if (!imgs.length) return;

    var ICON_CLOSE = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>';
    var ICON_PREV = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>';
    var ICON_NEXT = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>';

    var lb = document.createElement("div");
    lb.className = "lightbox";
    lb.setAttribute("role", "dialog");
    lb.setAttribute("aria-modal", "true");
    lb.innerHTML =
        '<button class="lb-close" aria-label="Schließen">' + ICON_CLOSE + "</button>" +
        '<button class="lb-btn lb-prev" aria-label="Zurück">' + ICON_PREV + "</button>" +
        '<img alt="">' +
        '<button class="lb-btn lb-next" aria-label="Weiter">' + ICON_NEXT + "</button>" +
        '<div class="lb-cap"></div>';
    document.body.appendChild(lb);

    var big = lb.querySelector("img");
    var cap = lb.querySelector(".lb-cap");
    var idx = 0;

    function captionFor(el) {
        var fig = el.closest ? el.closest("figure") : null;
        var fc = fig ? fig.querySelector("figcaption") : null;
        if (fc) return fc.innerHTML;
        return el.alt || "";
    }

    function show(i) {
        idx = (i + imgs.length) % imgs.length;
        var el = imgs[idx];
        big.src = el.currentSrc || el.src;
        big.alt = el.alt || "";
        cap.innerHTML = captionFor(el);
    }

    function open(i) {
        show(i);
        lb.classList.add("open");
        document.body.style.overflow = "hidden";
    }

    function close() {
        lb.classList.remove("open");
        document.body.style.overflow = "";
    }

    imgs.forEach(function (el, i) {
        el.classList.add("zoomable");
        el.addEventListener("click", function () { open(i); });
    });

    lb.addEventListener("click", function (e) { if (e.target === lb) close(); });
    big.addEventListener("click", function (e) { e.stopPropagation(); });
    lb.querySelector(".lb-close").addEventListener("click", close);
    lb.querySelector(".lb-prev").addEventListener("click", function (e) { e.stopPropagation(); show(idx - 1); });
    lb.querySelector(".lb-next").addEventListener("click", function (e) { e.stopPropagation(); show(idx + 1); });

    document.addEventListener("keydown", function (e) {
        if (!lb.classList.contains("open")) return;
        if (e.key === "Escape") close();
        else if (e.key === "ArrowLeft") show(idx - 1);
        else if (e.key === "ArrowRight") show(idx + 1);
    });
})();

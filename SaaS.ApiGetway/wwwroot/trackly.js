(function () {
    // -------------------------
    // Config / Helpers
    // -------------------------
    const scriptTag = document.currentScript;
    const apiKey = scriptTag ? new URLSearchParams(scriptTag.src.split('?')[1]).get("key") : null;
    if (!apiKey) return console.error("Trackly: Missing API key in script src.");

    const API_SESSION_CREATE = "https://apibizagent.techciph.com/Session/create";
    const API_EVENT_TRACK = "https://apibizagent.techciph.com/Session/track";

    function setCookie(name, value, days) {
        let expires = "";
        if (days) {
            const d = new Date();
            d.setTime(d.getTime() + days * 24 * 60 * 60 * 1000);
            expires = "; expires=" + d.toUTCString();
        }
        document.cookie = `${name}=${encodeURIComponent(value || "")}${expires}; path=/; SameSite=Lax`;
    }
    function getCookie(name) {
        const m = document.cookie.match(new RegExp('(^| )' + name + '=([^;]+)'));
        return m ? decodeURIComponent(m[2]) : null;
    }

    // small util to safe parse float
    function toNumber(v) {
        if (v == null) return null;
        const s = String(v).replace(/[^0-9.\-]/g, "");
        const n = parseFloat(s);
        return isNaN(n) ? null : n;
    }

    // -------------------------
    // Session + event queue
    // -------------------------
    let sessionId = getCookie("trackly_session");
    let sessionReady = !!sessionId;
    const eventQueue = [];

    function flushQueue() {
        if (!sessionReady) return;
        while (eventQueue.length) {
            sendEvent(eventQueue.shift());
        }
    }

    function queueOrSend(payload) {
        if (!sessionReady) {
            eventQueue.push(payload);
        } else {
            sendEvent(payload);
        }
    }

    async function startSession() {
        const payload = {
            Ip: "",
            ReferrerUrl: document.referrer || null,
            UserAgent: navigator.userAgent,
            Language: navigator.language || navigator.userLanguage || null,
            Screen: `${window.screen.width}x${window.screen.height}`
        };
        try {
            const res = await fetch(API_SESSION_CREATE, {
                method: "POST",
                headers: { "Content-Type": "application/json", "X-API-KEY": apiKey },
                body: JSON.stringify(payload),
                credentials: "omit"
            });
            const data = await res.json();
            if (data && data.sessionId) {
                sessionId = data.sessionId;
                sessionReady = true;
                setCookie("trackly_session", sessionId, 1);
                flushQueue();
                return sessionId;
            } else {
                console.warn("Trackly: session create returned no sessionId", data);
            }
        } catch (err) {
            console.error("Trackly: startSession error", err);
        }
    }

    function sendEventObj(evt) {
        queueOrSend(evt);
    }

    function sendEvent({ event, data }) {
        if (!sessionId) {
            eventQueue.push({ event, data });
            return;
        }
        fetch(API_EVENT_TRACK, {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-API-KEY": apiKey },
            body: JSON.stringify({
                sessionId,
                event,
                data,
                url: window.location.href,
                ReferrerUrl: document.referrer || null,
                ts: new Date().toISOString()
            })
        }).catch(e => {
            console.error("Trackly: sendEvent failed", e);
        });
    }

    // -------------------------
    // Category detection
    // -------------------------
    function detectCategoryFromBreadcrumbs() {
        try {
            const crumbs = document.querySelectorAll(".breadcrumb li, nav.breadcrumb li, nav ol li, .breadcrumbs li");
            if (crumbs && crumbs.length > 0) {
                const text = crumbs[crumbs.length - 1].innerText.trim();
                if (text) return text;
            }
            const meta = document.querySelector('meta[property="product:category"], meta[name="category"]');
            if (meta) return meta.content;
            // URL path guess
            const parts = location.pathname.split("/").filter(p => p && p.length > 1);
            if (parts.length >= 2) return parts[parts.length - 1];
        } catch (e) { /* ignore */ }
        return "Unknown";
    }

    // -------------------------
    // Product extraction heuristics
    // -------------------------
    // Common product container selectors used by templates
    const PRODUCT_SELECTORS = [
        "[data-product-id]",
        ".product-card",
        ".product",
        ".product-item",
        ".productTile",
        ".grid-product",
        ".product-list-item",
        "article[data-id]",
    ];

    // keep track of processed nodes to avoid duplicate listeners
    const processed = new WeakSet();

    function extractFromContainer(node) {
        // Return null if not product-like
        if (!node || processed.has(node)) return null;

        // Heuristic: look for price text inside node
        const txt = node.innerText || "";
        const priceRegex = /(?:¥|\$|₹|৳|€)\s?\d{1,3}(?:[.,]\d{2,})?/g;
        const priceMatch = txt.match(priceRegex);
        if (!priceMatch) {
            // also try patterns like 100.00 or 10000 (with currency missing) near price class names
            const alt = node.querySelector("[class*='price'], .product-price, .price")?.innerText;
            if (!alt) return null;
        }

        // id: try dataset, link href, or generate from name+price
        let id = node.dataset && (node.dataset.productId || node.dataset.id || node.dataset.sku) || null;
        if (!id) {
            const link = node.querySelector("a[href*='product'], a[href*='product-detail'], a[href*='sku'], a[href*='/p/']");
            if (link) {
                const m = link.href.match(/\/(product|p|item|sku)[\/-]?([^\/?#]+)/i);
                if (m && m[2]) id = decodeURIComponent(m[2]);
            }
        }

        // name
        const name =
            (node.querySelector("h1,h2,h3,h4,h5,h6,.product-title,.title")?.innerText ||
                node.querySelector("img")?.alt ||
                node.getAttribute("aria-label") ||
                node.dataset.name) || null;

        // price
        let price = null;
        if (priceMatch && priceMatch.length) price = toNumber(priceMatch[0]);
        else {
            const pEl = node.querySelector(".price, .product-price, [class*='price']");
            if (pEl) price = toNumber(pEl.innerText);
        }

        // category (best-effort: container scope fallback to page category)
        let category = node.dataset.category || node.getAttribute("data-category") || null;
        if (!category) category = detectCategoryFromBreadcrumbs();

        // quantity detection
        let quantity = 1;
        const qtyInput = node.querySelector('input[type="number"], input.qty, [name="quantity"], select.qty, select[name="quantity"]');
        if (qtyInput) {
            const v = parseInt(qtyInput.value || qtyInput.getAttribute("value") || "1");
            if (!isNaN(v) && v > 0) quantity = v;
        }

        // variants detection: size and color
        const variant = { size: null, color: null, attributes: {} };

        // select-based
        const sizeSelect = node.querySelector('select[name*="size"], select[id*="size"], select[data-variant*="size"]');
        if (sizeSelect) variant.size = sizeSelect.value || null;

        const colorSelect = node.querySelector('select[name*="color"], select[id*="color"], select[data-variant*="color"]');
        if (colorSelect) variant.color = colorSelect.value || null;

        // button-based (swatches)
        const sizeBtn = Array.from(node.querySelectorAll("button, a")).find(b => {
            const t = (b.innerText || "").trim().toLowerCase();
            return ["s", "m", "l", "xl", "xs", "xxl"].includes(t);
        });
        if (sizeBtn && !variant.size) variant.size = sizeBtn.innerText.trim();

        // color swatch (background-color or aria-label)
        const colorBtn = Array.from(node.querySelectorAll("[data-color], [aria-label]")).find(el => {
            const al = el.getAttribute("aria-label");
            return al && /color/i.test(al);
        });
        if (colorBtn && !variant.color) variant.color = colorBtn.getAttribute("aria-label") || null;

        // fallback: look for attributes like data-variant-*
        for (const k in node.dataset) {
            if (/variant|option|attr|attribute/i.test(k)) {
                variant.attributes[k] = node.dataset[k];
            }
        }

        // image
        const image = node.querySelector("img")?.src || null;

        // if nothing meaningful found, skip
        if (!name && !price && !id) return null;

        // mark processed to avoid double-binding listeners
        processed.add(node);

        return {
            id: id || (name ? `gen_${slugify(name)}_${Math.round((price || 0) * 100)}` : null),
            name: name || null,
            price: price,
            category: category || null,
            quantity: quantity,
            variant: variant,
            image: image
        };
    }

    // small slug helper
    function slugify(s) {
        return String(s).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
    }

    // -------------------------
    // Auto bind product buttons
    // -------------------------
    function bindProductInteractions(container, product) {
        if (!product || !container) return;
        // scan for actionable controls
        const buttons = container.querySelectorAll("button,a,input[type=button],input[type=submit]");
        buttons.forEach(btn => {
            // normalize text
            const txt = (btn.innerText || btn.value || "").trim().toLowerCase();
            if (!txt) return;
            // add to cart
            if (/add to cart|add to basket|add cart|add to bag|bag it|cart add/i.test(txt)) {
                btn.addEventListener("click", () => {
                    sendEvent({ event: "add_to_cart", data: product });
                });
            }
            // buy / checkout / purchase
            if (/buy now|buy|checkout|place order|purchase/i.test(txt)) {
                btn.addEventListener("click", () => {
                    sendEvent({ event: "purchase", data: product });
                });
            }
        });
    }

    // -------------------------
    // Scan page for products
    // -------------------------
    function scanAndTrackProducts() {
        // try specific selectors first
        let nodes = [];
        PRODUCT_SELECTORS.forEach(sel => nodes.push(...Array.from(document.querySelectorAll(sel))));
        // fallback to scanning common containers
        if (!nodes.length) nodes = Array.from(document.querySelectorAll("article, section, div")).slice(0, 500);

        nodes.forEach(node => {
            const product = extractFromContainer(node);
            if (product) {
                // send view_item event
                sendEvent({ event: "view_item", data: product });
                bindProductInteractions(node, product);
            }
        });
    }

    // -------------------------
    // Mutation observer for AJAX
    // -------------------------
    const mo = new MutationObserver((mutations) => {
        for (const m of mutations) {
            if (m.addedNodes && m.addedNodes.length) {
                m.addedNodes.forEach(n => {
                    if (!(n instanceof HTMLElement)) return;
                    // if this node looks like a product container
                    const p = extractFromContainer(n) || Array.from(n.querySelectorAll("div,article,section")).map(extractFromContainer).find(x => x);
                    if (p) {
                        sendEvent({ event: "view_item", data: p });
                        // bind interactions for inner node
                        const container = (n.matches && n.matches("[data-product-id], .product-card, .product, .product-item")) ? n : n.querySelector(".product-card, .product, .product-item") || n;
                        bindProductInteractions(container, p);
                    }
                });
            }
        }
    });
    mo.observe(document.body, { childList: true, subtree: true });

    // -------------------------
    // Basic auto events (links, buttons, forms, scroll)
    // -------------------------
    function enableAutoEvents() {
        // page_view
        sendEvent({ event: "page_view", data: { title: document.title } });

        // links
        document.addEventListener("click", (e) => {
            const a = e.target.closest("a");
            if (a) sendEvent({ event: "link_click", data: { text: a.innerText, href: a.href } });
            const btn = e.target.closest("button");
            if (btn) sendEvent({ event: "button_click", data: { text: btn.innerText || btn.value } });
        }, { passive: true });

        // forms
        document.addEventListener("submit", (e) => {
            const f = e.target;
            sendEvent({ event: "form_submit", data: { action: f.action, id: f.id || null, classes: f.className || null } });
        }, true);

        //// scroll depth
        //let maxScroll = 0;
        //window.addEventListener("scroll", () => {
        //    const cur = Math.round((window.scrollY / (document.body.scrollHeight - window.innerHeight)) * 100);
        //    if (cur > maxScroll) {
        //        maxScroll = cur;
        //        sendEvent({ event: "scroll_depth", data: { percent_scrolled: maxScroll } });
        //    }
        //}, { passive: true });
    }

    // -------------------------
    // Public API
    // -------------------------
    window.Trackly = window.Trackly || {};
    window.Trackly.track = function (event, data) {
        sendEvent({ event, data });
    };
    window.Trackly.trackProduct = function (action, product) {
        if (!product) return;
        sendEvent({ event: action, data: product });
    };

    // -------------------------
    // Init
    // -------------------------
    if (!sessionReady) {
        startSession().then(() => {
            scanAndTrackProducts();
            enableAutoEvents();
        });
    } else {
        scanAndTrackProducts();
        enableAutoEvents();
    }

    // expose small debug helper
    window.Trackly._debugFlushQueue = flushQueue;
})();




//(function () {
//    // Extract API Key from script tag URL
//    const scriptTag = document.currentScript;
//    const apiKey = new URLSearchParams(scriptTag.src.split('?')[1]).get("key");

//    if (!apiKey) {
//        console.error("Trackly: Missing x-api-key!");
//        return;
//    }

//    // Helper: Save cookie
//    function setCookie(name, value, days) {
//        let expires = "";
//        if (days) {
//            const date = new Date();
//            date.setTime(date.getTime() + days * 24 * 60 * 60 * 1000);
//            expires = "; expires=" + date.toUTCString();
//        }
//        document.cookie = name + "=" + (value || "") + expires + "; path=/; Secure; SameSite=Lax";
//    }

//    function getCookie(name) {
//        const nameEQ = name + "=";
//        const ca = document.cookie.split(";");
//        for (let i = 0; i < ca.length; i++) {
//            let c = ca[i];
//            while (c.charAt(0) === " ") c = c.substring(1, c.length);
//            if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length, c.length);
//        }
//        return null;
//    }

//    // Start session if not exists
//    let sessionId = getCookie("trackly_session");

//    function startSession() {
//        const payload = {
//            Ip: "",
//            ReferrerUrl: document.referrer,
//            UserAgent: navigator.userAgent,
//            Language: navigator.language || navigator.userLanguage,
//            Screen: `${window.screen.width}x${window.screen.height}`
//        };

//        return fetch("https://apibizagent.techciph.com/Session/create", {
//            method: "POST",
//            headers: {
//                "Content-Type": "application/json",
//                "X-API-KEY": apiKey
//            },
//            body: JSON.stringify(payload)
//        })
//            .then(res => res.json())
//            .then(result => {
//                if (result.sessionId) {
//                    sessionId = result.sessionId;
//                    setCookie("trackly_session", sessionId, 1);
//                    console.log("Trackly session started:", sessionId);
//                    return sessionId;
//                }
//            })
//            .catch(err => console.error("Trackly init failed:", err));
//    }

//    // Event tracking function
//    window.Trackly = {
//        track: function (eventName, data = {}) {
//            if (!sessionId) return;

//            fetch("https://apibizagent.techciph.com/Session/track", {
//                method: "POST",
//                headers: {
//                    "Content-Type": "application/json",
//                    "X-API-KEY": apiKey
//                },
//                body: JSON.stringify({
//                    sessionId,
//                    event: eventName,
//                    data,
//                    url: window.location.href,
//                    ReferrerUrl: document.referrer,
//                    ts: new Date().toISOString()
//                })
//            }).catch(err => console.error("Trackly event failed:", err));
//        }
//    };

//    // AUTO EVENT TRACKING
//    function enableAutoTracking() {

//        // PAGE VIEW
//        window.Trackly.track("page_view", {
//            title: document.title
//        });

//        // LINK CLICK
//        document.addEventListener("click", function (e) {
//            let el = e.target.closest("a");
//            if (!el) return;

//            Trackly.track("link_click", {
//                text: el.innerText,
//                href: el.href
//            });
//        });

//        // BUTTON CLICK
//        document.addEventListener("click", function (e) {
//            let el = e.target.closest("button");
//            if (!el) return;

//            Trackly.track("button_click", {
//                text: el.innerText || el.value
//            });
//        });

//        // FORM SUBMIT
//        document.addEventListener("submit", function (e) {
//            let form = e.target;
//            Trackly.track("form_submit", {
//                action: form.action,
//                id: form.id,
//                classes: form.className
//            });
//        });

//        // SCROLL DEPTH
//        let maxScroll = 0;
//        window.addEventListener("scroll", function () {
//            let current = Math.round(
//                (window.scrollY / (document.body.scrollHeight - window.innerHeight)) * 100
//            );

//            if (current > maxScroll) {
//                maxScroll = current;
//                Trackly.track("scroll_depth", { depth: maxScroll });
//            }
//        });
//    }

//    // Initialize
//    if (!sessionId) {
//        startSession().then(() => enableAutoTracking());
//    } else {
//        enableAutoTracking();
//    }

//})();

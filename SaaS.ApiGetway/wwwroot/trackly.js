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
    function toNumber(v) {
        if (v == null) return null;
        const s = String(v).replace(/[^0-9.\-]/g, "");
        const n = parseFloat(s);
        return isNaN(n) ? null : n;
    }
    function nowMs() { return Date.now(); }
    function safeJSON(v) {
        try { return JSON.stringify(v); } catch { return String(v); }
    }

    // -------------------------
    // Session + queue + dedupe
    // -------------------------
    let sessionId = getCookie("trackly_session");
    let sessionReady = !!sessionId;
    const eventQueue = [];
    const lastSeen = new Map(); // key -> timestamp ms
    const DEDUPE_WINDOW_MS = 2000; // ignore duplicates within 2s (configurable)
    const ONE_TIME_EVENTS = new Set(); // keys for page-level one-time events

    function dedupeKey(event, data) {
        // Build small signature: event + id or name + minimal props
        const id = data && (data.id || data.productId || data.transaction_id || data.transactionId || data.name || "");
        return `${event}::${id}`;
    }

    function shouldSend(event, data) {
        const key = dedupeKey(event, data);
        const t = nowMs();
        const last = lastSeen.get(key) || 0;
        if (t - last < DEDUPE_WINDOW_MS) return false;
        lastSeen.set(key, t);
        return true;
    }

    function queueOrSend(payload) {
        if (!sessionReady) eventQueue.push(payload);
        else sendEvent(payload);
    }

    function flushQueue() {
        if (!sessionReady) return;
        while (eventQueue.length) sendEvent(eventQueue.shift());
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

    function sendEvent(payload) {
        if (!sessionId) {
            eventQueue.push(payload);
            return;
        }
        // dedupe check
        if (!shouldSend(payload.event, payload.data)) return;
        // send
        fetch(API_EVENT_TRACK, {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-API-KEY": apiKey },
            body: JSON.stringify({
                sessionId,
                event: payload.event,
                data: payload.data,
                url: window.location.href,
                ReferrerUrl: document.referrer || null,
                ts: new Date().toISOString()
            })
        }).catch(e => console.error("Trackly: sendEvent failed", e));
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
            const parts = location.pathname.split("/").filter(p => p && p.length > 1);
            if (parts.length >= 2) return parts[parts.length - 1];
        } catch (e) { /* ignore */ }
        return "Unknown";
    }

    // -------------------------
    // Product extraction heuristics
    // -------------------------
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
    const processed = new WeakSet();

    function slugify(s) {
        return String(s).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
    }

    function extractFromContainer(node) {
        if (!node || processed.has(node)) return null;

        const txt = node.innerText || "";
        const priceRegex = /(?:¥|\$|₹|৳|€)\s?\d{1,3}(?:[.,]\d{2,})?/g;
        const priceMatch = txt.match(priceRegex);
        if (!priceMatch) {
            const alt = node.querySelector("[class*='price'], .product-price, .price")?.innerText;
            if (!alt) return null;
        }

        let id = node.dataset && (node.dataset.productId || node.dataset.id || node.dataset.sku) || null;
        if (!id) {
            const link = node.querySelector("a[href*='product'], a[href*='product-detail'], a[href*='sku'], a[href*='/p/']");
            if (link) {
                const m = link.href.match(/\/(product|p|item|sku)[\/-]?([^\/?#]+)/i);
                if (m && m[2]) id = decodeURIComponent(m[2]);
            }
        }

        const name =
            (node.querySelector("h1,h2,h3,h4,h5,h6,.product-title,.title")?.innerText ||
                node.querySelector("img")?.alt ||
                node.getAttribute("aria-label") ||
                node.dataset.name) || null;

        let price = null;
        if (priceMatch && priceMatch.length) price = toNumber(priceMatch[0]);
        else {
            const pEl = node.querySelector(".price, .product-price, [class*='price']");
            if (pEl) price = toNumber(pEl.innerText);
        }

        let category = node.dataset.category || node.getAttribute("data-category") || null;
        if (!category) category = detectCategoryFromBreadcrumbs();

        let quantity = 1;
        const qtyInput = node.querySelector('input[type="number"], input.qty, [name="quantity"], select.qty, select[name="quantity"]');
        if (qtyInput) {
            const v = parseInt(qtyInput.value || qtyInput.getAttribute("value") || "1");
            if (!isNaN(v) && v > 0) quantity = v;
        }

        const variant = { size: null, color: null, attributes: {} };
        const sizeSelect = node.querySelector('select[name*="size"], select[id*="size"], select[data-variant*="size"]');
        if (sizeSelect) variant.size = sizeSelect.value || null;
        const colorSelect = node.querySelector('select[name*="color"], select[id*="color"], select[data-variant*="color"]');
        if (colorSelect) variant.color = colorSelect.value || null;
        const sizeBtn = Array.from(node.querySelectorAll("button, a")).find(b => {
            const t = (b.innerText || "").trim().toLowerCase();
            return ["s", "m", "l", "xl", "xs", "xxl"].includes(t);
        });
        if (sizeBtn && !variant.size) variant.size = sizeBtn.innerText.trim();
        const colorBtn = Array.from(node.querySelectorAll("[data-color], [aria-label]")).find(el => {
            const al = el.getAttribute("aria-label");
            return al && /color/i.test(al);
        });
        if (colorBtn && !variant.color) variant.color = colorBtn.getAttribute("aria-label") || null;
        for (const k in node.dataset) {
            if (/variant|option|attr|attribute/i.test(k)) variant.attributes[k] = node.dataset[k];
        }

        const image = node.querySelector("img")?.src || null;
        if (!name && !price && !id) return null;

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

    // -------------------------
    // LIST SCAN (view_item_list)
    // -------------------------
    function buildItemListFromNodes(nodes) {
        const items = [];
        nodes.forEach(n => {
            const p = extractFromContainer(n);
            if (p) items.push(p);
        });
        return items;
    }

    function scanListsAndSend() {
        // Find product lists on common selectors
        const listSelectors = [
            ".products", ".product-list", ".product-grid", ".listing", ".collection", ".woocommerce", ".search-results"
        ];
        const lists = new Set();
        listSelectors.forEach(sel => document.querySelectorAll(sel).forEach(n => lists.add(n)));
        // fallback: group nodes by parent that contain multiple products
        if (!lists.size) {
            const all = Array.from(document.querySelectorAll(PRODUCT_SELECTORS.join(",")));
            const grouped = new Map();
            all.forEach(n => {
                const p = n.parentElement || document.body;
                const key = p;
                if (!grouped.has(key)) grouped.set(key, []);
                grouped.get(key).push(n);
            });
            grouped.forEach((arr, parent) => {
                if (arr.length >= 2) lists.add(parent);
            });
        }

        lists.forEach(listEl => {
            const itemNodes = Array.from(listEl.querySelectorAll(PRODUCT_SELECTORS.join(",")));
            const items = buildItemListFromNodes(itemNodes);
            if (items && items.length) {
                // dedupe using list container path + item ids
                const key = `view_item_list::${listEl.tagName}::${listEl.className}::${location.pathname}`;
                if (!ONE_TIME_EVENTS.has(key)) {
                    ONE_TIME_EVENTS.add(key);
                    sendEvent({ event: "view_item_list", data: { items, list_name: listEl.className || null } });
                }
            }
        });
    }

    // -------------------------
    // Bind product interactions
    // -------------------------
    function bindProductInteractions(container, product) {
        if (!product || !container) return;
        const buttons = container.querySelectorAll("button,a,input[type=button],input[type=submit]");
        buttons.forEach(btn => {
            const txt = (btn.innerText || btn.value || "").trim().toLowerCase();
            if (!txt) return;
            if (/add to cart|add to basket|add cart|add to bag|bag it|cart add|add-to-cart/i.test(txt)) {
                btn.addEventListener("click", () => sendEvent({ event: "add_to_cart", data: product }));
            }
            if (/buy now|buy|checkout|place order|purchase|complete order|pay now/i.test(txt)) {
                btn.addEventListener("click", () => {
                    // on buy immediately send purchase with generated txn if not present
                    const txn = { transaction_id: `txn_${nowMs()}`, items: [product], value: product.price, currency: (product.currency || "USD") };
                    sendEvent({ event: "purchase", data: txn });
                });
            }
            if (/checkout|proceed to checkout|go to checkout/i.test(txt)) {
                btn.addEventListener("click", () => sendEvent({ event: "begin_checkout", data: { items: [product] } }));
            }
        });
    }

    // -------------------------
    // Scan page for products & bind
    // -------------------------
    function scanAndBindAll() {
        let nodes = [];
        PRODUCT_SELECTORS.forEach(sel => nodes.push(...Array.from(document.querySelectorAll(sel))));
        if (!nodes.length) nodes = Array.from(document.querySelectorAll("article, section, div")).slice(0, 500);
        nodes.forEach(node => {
            const p = extractFromContainer(node);
            if (p) {
                sendEvent({ event: "view_item", data: p });
                bindProductInteractions(node, p);
            }
        });
        // view_item_list
        scanListsAndSend();
    }

    // -------------------------
    // MutationObserver for AJAX loaded products
    // -------------------------
    const mo = new MutationObserver((mutations) => {
        for (const m of mutations) {
            if (m.addedNodes && m.addedNodes.length) {
                m.addedNodes.forEach(n => {
                    if (!(n instanceof HTMLElement)) return;
                    // try to extract product from newly added node or its children
                    const p = extractFromContainer(n) || Array.from(n.querySelectorAll("div,article,section")).map(extractFromContainer).find(x => x);
                    if (p) {
                        sendEvent({ event: "view_item", data: p });
                        const container = (n.matches && n.matches("[data-product-id], .product-card, .product, .product-item")) ? n : n.querySelector(".product-card, .product, .product-item") || n;
                        bindProductInteractions(container, p);
                    } else {
                        // also re-scan lists in case a list updated
                        scanListsAndSend();
                    }
                });
            }
        }
    });
    mo.observe(document.body, { childList: true, subtree: true });

    // -------------------------
    // Basic auto events (links, buttons, forms)
    // -------------------------
    function enableAutoEvents() {
        // page_view (one time)
        const pvKey = `page_view::${location.pathname}`;
        if (!ONE_TIME_EVENTS.has(pvKey)) {
            ONE_TIME_EVENTS.add(pvKey);
            sendEvent({ event: "page_view", data: { title: document.title } });
        }

        // link/button clicks (generic)
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

        // detect begin_checkout by URL or presence of checkout forms
        if (/checkout|cart|order/.test(location.pathname.toLowerCase())) {
            const bcKey = `begin_checkout::${location.pathname}`;
            if (!ONE_TIME_EVENTS.has(bcKey)) {
                ONE_TIME_EVENTS.add(bcKey);
                // try to collect cart items if present in DOM (best-effort)
                const cartItems = Array.from(document.querySelectorAll(".cart-item, .cart-row, .cart-product, [data-cart-item]")).map(el => {
                    return extractFromContainer(el) || { name: el.innerText?.slice(0, 80) || "cart_item" };
                }).filter(Boolean);
                sendEvent({ event: "begin_checkout", data: { items: cartItems } });
            }
        }

        // purchase detection by thank-you page / URL patterns (one time)
        if (/thank-you|order-confirmation|order-received|order\/\d+|checkout\/complete|success/.test(location.pathname.toLowerCase())) {
            const puKey = `purchase_page::${location.pathname}`;
            if (!ONE_TIME_EVENTS.has(puKey)) {
                ONE_TIME_EVENTS.add(puKey);
                // try to extract transaction details (site-specific)
                // best-effort: look for order id / totals
                const orderId = document.querySelector(".order-id, #order-id, [data-order-id]")?.innerText?.trim()
                    || document.querySelector("[data-transaction-id]")?.getAttribute("data-transaction-id")
                    || `txn_${nowMs()}`;
                const totalText = document.querySelector(".order-total, .total, [data-order-total]")?.innerText || null;
                const total = toNumber(totalText) || null;
                // try cart items
                const items = Array.from(document.querySelectorAll(".order-item, .purchased-item, [data-order-item]")).map(el => extractFromContainer(el)).filter(Boolean);
                sendEvent({ event: "purchase", data: { transaction_id: orderId, value: total, currency: "USD", items } });
            }
        }
    }

    // -------------------------
    // Public API
    // -------------------------
    window.Trackly = window.Trackly || {};
    window.Trackly.track = function (event, data) { sendEvent({ event, data }); };
    window.Trackly.trackProduct = function (action, product) { if (!product) return; sendEvent({ event: action, data: product }); };

    // -------------------------
    // Init
    // -------------------------
    if (!sessionReady) {
        startSession().then(() => {
            scanAndBindAll();
            enableAutoEvents();
        });
    } else {
        scanAndBindAll();
        enableAutoEvents();
    }

    // small debug helpers
    window.Trackly._flushQueue = flushQueue;
    window.Trackly._lastSeen = lastSeen;
    window.Trackly._oneTime = ONE_TIME_EVENTS;

})();

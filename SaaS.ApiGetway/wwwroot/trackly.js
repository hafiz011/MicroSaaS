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
        document.cookie = `${name}=${encodeURIComponent(value || "")}${expires}; path=/; SameSite=Lax; Secure`;
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

    // ======================== AUTO CURRENCY DETECT ========================
    let detectedCurrency = null;
    function detectCurrency() {
        if (detectedCurrency) return detectedCurrency;

        const symbolMap = { '$': 'USD', '£': 'GBP', '€': 'EUR', '¥': 'JPY', '৳': 'BDT', '₹': 'INR', '₽': 'RUB', '₩': 'KRW' };
        const matches = document.body.innerText.match(/[\$£€¥৳₹₽₩]\s*[\d.,]+/g) || [];
        for (const m of matches) {
            const sym = m.replace(/[\d.,\s]/g, '').trim();
            if (symbolMap[sym]) return detectedCurrency = symbolMap[sym];
        }

        const meta = document.querySelector('meta[property="product:price:currency"], meta[name="currency"]');
        if (meta?.content) return detectedCurrency = meta.content.trim().toUpperCase();

        const host = location.hostname.toLowerCase();
        if (host.includes('.bd')) return detectedCurrency = 'BDT';
        if (host.includes('.in')) return detectedCurrency = 'INR';
        return detectedCurrency = 'USD';
    }

    // -------------------------
    // Session + queue + dedupe
    // -------------------------
    let sessionId = getCookie("trackly_session");
    let sessionReady = !!sessionId;
    const eventQueue = [];
    const lastSeen = new Map();
    const DEDUPE_WINDOW_MS = 2500;
    const ONE_TIME_EVENTS = new Set();

    function dedupeKey(event, data) {
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
            Language: navigator.language || null,
            Screen: `${screen.width}x${screen.height}`
        };

        try {
            const res = await fetch(API_SESSION_CREATE, {
                method: "POST",
                headers: { "Content-Type": "application/json", "X-API-KEY": apiKey },
                body: JSON.stringify(payload)
            });
            const data = await res.json();

            if (data && data.sessionId) {
                sessionId = data.sessionId;
                sessionReady = true;
                setCookie("trackly_session", sessionId, 30);
                flushQueue();
                console.log("%cTrackly: Session created & cookie set", "color: green; font-weight: bold");
                return sessionId;
            }
        } catch (err) {
            console.error("Trackly: Session create failed", err);
        }
    }

    function sendEvent(payload) {
        if (!sessionId) {
            eventQueue.push(payload);
            return;
        }

        if (!shouldSend(payload.event, payload.data)) return;

        const body = {
            sessionId,
            event: payload.event,
            data: payload.data,
            url: window.location.href,
            ReferrerUrl: document.referrer || null,
            ts: new Date().toISOString()
        };

        navigator.sendBeacon?.(API_EVENT_TRACK, JSON.stringify(body)) || fetch(API_EVENT_TRACK, {
            method: "POST",
            headers: { "Content-Type": "application/json", "X-API-KEY": apiKey },
            body: JSON.stringify(body),
            keepalive: true
        }).catch(() => { });
    }

    // -------------------------
    // Product extraction (currency + category updated)
    // -------------------------
    const PRODUCT_SELECTORS = "[data-product-id],.product-card,.product,.product-item,.productTile,.grid-product,.product-list-item,article[data-id]";
    const processed = new WeakSet();

    function extractFromContainer(node) {
        if (!node || processed.has(node)) return null;
        processed.add(node);

        const name = node.querySelector("h1,h2,h3,.product-title,.title,img")?.innerText?.trim() || node.querySelector("img")?.alt || null;
        const priceEl = node.querySelector(".price,.product-price,[class*='price'],money");
        const price = priceEl ? toNumber(priceEl.innerText) : null;
        const id = node.dataset.productId || node.dataset.id || node.dataset.sku || null;

        if (!name && !price && !id) return null;

        let quantity = 1;
        const qtyInput = node.querySelector('input[name="quantity"],input.qty,input[type="number"]') || document.querySelector('input[name="quantity"]');
        if (qtyInput) quantity = parseInt(qtyInput.value || "1", 10) || 1;

        const variant = {};
        const form = node.closest("form") || document;
        form.querySelectorAll("select").forEach(s => {
            if (s.value && s.value !== "Default Title") {
                const key = /size/i.test(s.name) ? "size" : /color/i.test(s.name) ? "color" : s.name;
                variant[key] = s.options[s.selectedIndex].text.trim();
            }
        });

        return {
            id: id || `gen_${nowMs()}`,
            name,
            price,
            currency: detectCurrency(),
            category: detectCategoryFromBreadcrumbs(),
            quantity,
            variant: Object.keys(variant).length ? variant : null,
            image: node.querySelector("img")?.src || null
        };
    }

    function detectCategoryFromBreadcrumbs() {
        const bc = document.querySelector(".breadcrumb li:last-child, .breadcrumbs li:last-child, [aria-current='page']");
        if (bc?.innerText.trim()) return bc.innerText.trim();
        const parts = location.pathname.split("/").filter(p => p);
        return parts.length > 1 ? parts[parts.length - 2] : "Unknown";
    }

    // -------------------------
    // Scan & Bind
    // -------------------------
    function scanAndBindAll() {
        const nodes = Array.from(document.querySelectorAll(PRODUCT_SELECTORS));
        nodes.forEach(node => {
            const p = extractFromContainer(node);
            if (p) {
                queueOrSend({ event: "view_item", data: p });
                // Add to cart bind
                node.querySelectorAll("button,a").forEach(btn => {
                    const txt = (btn.innerText || "").toLowerCase();
                    if (/add|cart|basket/i.test(txt) && !/checkout/i.test(txt)) {
                        btn.addEventListener("click", () => {
                            p.quantity = parseInt(document.querySelector('input[name="quantity"]')?.value || "1") || 1;
                            p.currency = detectCurrency();
                            queueOrSend({ event: "add_to_cart", data: p });
                        });
                    }
                });
            }
        });
        scanListsAndSend();
    }

    function scanListsAndSend() {
        const items = Array.from(document.querySelectorAll(PRODUCT_SELECTORS))
            .map(extractFromContainer)
            .filter(Boolean)
            .slice(0, 50);

        if (items.length > 2) {
            const key = `view_item_list::${location.pathname}`;
            if (!ONE_TIME_EVENTS.has(key)) {
                ONE_TIME_EVENTS.add(key);
                items.forEach(i => i.currency = detectCurrency());
                queueOrSend({ event: "view_item_list", data: { items, list_name: "collection" } });
            }
        }
    }

    // -------------------------
    // Auto Events + Purchase + Checkout
    // -------------------------
    function enableAutoEvents() {
        const pvKey = `page_view::${location.pathname}`;
        if (!ONE_TIME_EVENTS.has(pvKey)) {
            ONE_TIME_EVENTS.add(pvKey);
            queueOrSend({ event: "page_view", data: { title: document.title, currency: detectCurrency() } });
        }

        if (/cart|checkout|order/i.test(location.pathname)) {
            const key = `begin_checkout::${location.pathname}`;
            if (!ONE_TIME_EVENTS.has(key)) {
                ONE_TIME_EVENTS.add(key);
                const items = Array.from(document.querySelectorAll(".cart-item,.line-item")).map(extractFromContainer).filter(Boolean);
                items.forEach(i => i.currency = detectCurrency());
                if (items.length) queueOrSend({ event: "begin_checkout", data: { items } });
            }
        }

        if (/thank|success|confirmation|order/i.test(location.pathname)) {
            const key = `purchase::${location.pathname}`;
            if (!ONE_TIME_EVENTS.has(key)) {
                ONE_TIME_EVENTS.add(key);
                const items = Array.from(document.querySelectorAll(".order-item,.line-item")).map(extractFromContainer).filter(Boolean);
                items.forEach(i => i.currency = detectCurrency());
                queueOrSend({ event: "purchase", data: { transaction_id: `txn_${nowMs()}`, value: null, currency: detectCurrency(), items } });
            }
        }
    }

    // -------------------------
    // Mutation Observer (AJAX)
    // -------------------------
    const mo = new MutationObserver(muts => {
        for (const m of muts) {
            m.addedNodes.forEach(n => {
                if (n instanceof HTMLElement) {
                    scanAndBindAll();
                    scanListsAndSend();
                }
            });
        }
    });
    mo.observe(document.body, { childList: true, subtree: true });

    // -------------------------
    // Public API
    // -------------------------
    window.Trackly = window.Trackly || {};
    window.Trackly.track = function (event, data) { queueOrSend({ event, data }); };
    window.Trackly.getCurrency = detectCurrency;

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

    // Debug
    window.Trackly._debug = { sessionId: () => sessionId, cookie: () => getCookie("trackly_session") };
    console.log("%cTrackly Loaded – Pure Cookie System (30 days)", "color: #ff00ff; font-weight: bold");
})();
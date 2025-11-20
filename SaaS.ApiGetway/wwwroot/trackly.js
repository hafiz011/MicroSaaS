(function () {
    'use strict';

    const script = document.currentScript;
    const params = new URLSearchParams(script.src.split('?')[1]);
    const API_KEY = params.get('key');

    if (!API_KEY) return console.error('Trackly: API key missing');

    const ENDPOINT_SESSION = 'https://apibizagent.techciph.com/Session/create';
    const ENDPOINT_TRACK = 'https://apibizagent.techciph.com/Session/track';

    let sessionId = null;
    let ready = false;
    const queue = [];
    const dedupe = new Map();
    const oneTime = new Set();
    const DEDUPE_MS = 3000;

    // ---------------- COOKIE ----------------
    function getCookie(n) {
        const v = document.cookie.match('(^|;) ?' + n + '=([^;]*)(;|$)');
        return v ? v[2] : null;
    }

    // ---------------- Currency Detect ----------------
    let currency = null;
    function detectCurrency() {
        if (currency) return currency;

        const symbols = { '$': 'USD', '৳': 'BDT', '₹': 'INR', '€': 'EUR', '£': 'GBP', '¥': 'JPY' };
        const text = document.body.innerText;

        const match = text.match(/[\$৳₹€£¥]/);
        if (match) currency = symbols[match[0]];
        if (!currency) currency = location.hostname.endsWith('.bd') ? 'BDT' : 'USD';

        return currency;
    }

    // ---------------- SEND EVENT ----------------
    function send(payload) {
        if (!ready) return queue.push(payload);

        const current = getCookie('trk_sess');
        if (!current) return queue.push(payload);

        const key = `${payload.event}||${payload.data?.id || ''}`;
        if (dedupe.has(key) && Date.now() - dedupe.get(key) < DEDUPE_MS) return;
        dedupe.set(key, Date.now());

        fetch(ENDPOINT_TRACK, {
            method: 'POST',
            credentials: 'include',
            headers: {
                'Content-Type': 'application/json',
                'X-API-KEY': API_KEY
            },
            body: JSON.stringify({
                sessionId: current,
                Event: payload.event,
                Data: payload.data || {},
                Url: location.href,
                ReferrerUrl: document.referrer || null,
                Ts: new Date().toISOString()
            })
        }).catch(() => queue.push(payload));
    }

    function track(event, data = {}) {
        const k = `${event}::${location.pathname}`;
        if (oneTime.has(k)) return;

        if (['page_view', 'view_item_list', 'begin_checkout', 'purchase'].includes(event))
            oneTime.add(k);

        send({ event, data });
    }

    // ---------------- INIT SESSION ----------------
    async function initSession() {
        sessionId = getCookie('trk_sess');
        if (sessionId) {
            ready = true;
            flushQueue();
            startTracking();
            return;
        }

        try {
            const res = await fetch(ENDPOINT_SESSION, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                    'X-API-KEY': API_KEY
                },
                body: JSON.stringify({
                    Ip: "",
                    ReferrerUrl: document.referrer || null,
                    UserAgent: navigator.userAgent,
                    Language: navigator.language,
                    Screen: `${screen.width}x${screen.height}`
                })
            });

            if (res.ok) {
                sessionId = getCookie('trk_sess');
                if (sessionId) {
                    ready = true;
                    flushQueue();
                    startTracking();
                }
            }
        } catch (e) {
            setTimeout(initSession, 3000);
        }
    }

    function flushQueue() {
        while (queue.length) send(queue.shift());
    }

    // ---------------- PRODUCT EXTRACTOR ----------------
    const SELECTORS = [
        '[data-product-id]', '.product', '.product-item', '.product-card',
        '.woocommerce-product', '.product-grid-item'
    ].join(',');

    function extractProduct(card) {
        if (!card) return null;

        const name = card.querySelector('h1,h2,h3,.title,.product-title')?.innerText?.trim()
            || card.querySelector('img')?.alt || "Unknown";

        const priceEl = card.querySelector('.price, .amount, .woocommerce-Price-amount');
        const priceText = priceEl?.innerText || "";
        const price = parseFloat(priceText.replace(/[^0-9.]/g, "")) || null;

        const id =
            card.dataset.productId ||
            card.querySelector('[data-product-id]')?.dataset.productId ||
            card.querySelector('input[name="product_id"]')?.value ||
            `gen_${Date.now()}`;

        return {
            id,
            name,
            price,
            currency: detectCurrency(),
            category: document.querySelector('.breadcrumb li:last-child')?.innerText?.trim() || "Unknown",
            quantity: 1,
            image: card.querySelector('img')?.src || null
        };
    }

    // ---------------- TRACKING ENGINE ----------------
    function startTracking() {
        track('page_view', { title: document.title, currency: detectCurrency() });

        const products = document.querySelectorAll(SELECTORS);

        // View list
        if (products.length > 3 && !oneTime.has('view_item_list')) {
            track('view_item_list', {
                items: Array.from(products).slice(0, 40).map(extractProduct).filter(Boolean),
                list_name: "collection"
            });
        }

        products.forEach(card => {
            const p = extractProduct(card);
            if (!p) return;

            // PDP
            if (location.pathname.includes('/product')) {
                track('view_item', p);
            }

            // Add to cart
            card.querySelectorAll('button, input[type="submit"]').forEach(btn => {
                const txt = (btn.innerText || btn.value || "").toLowerCase();
                if (txt.includes("add") || txt.includes("cart")) {
                    btn.addEventListener('click', () => {
                        p.quantity = parseInt(document.querySelector('input.qty')?.value || 1) || 1;
                        track('add_to_cart', p);
                    });
                }
            });
        });

        // Begin checkout
        if (location.pathname.includes("checkout") && !oneTime.has('begin_checkout')) {
            const items = Array.from(document.querySelectorAll('.cart-item')).map(extractProduct).filter(Boolean);
            track('begin_checkout', { items });
        }

        // Purchase
        if (location.pathname.includes("thank") && !oneTime.has('purchase')) {
            const items = Array.from(document.querySelectorAll('.order-item')).map(extractProduct).filter(Boolean);
            track('purchase', {
                transaction_id: document.querySelector('.order-number')?.innerText || `txn_${Date.now()}`,
                items,
                currency: detectCurrency()
            });
        }
    }

    // ---------------- Observer (AJAX Reload Support) ----------------
    const observer = new MutationObserver(() => setTimeout(startTracking, 600));
    observer.observe(document.body, { childList: true, subtree: true });

    history.pushState = new Proxy(history.pushState, {
        apply(target, thisArg, argArray) {
            const r = Reflect.apply(target, thisArg, argArray);
            setTimeout(startTracking, 600);
            return r;
        }
    });
    window.addEventListener("popstate", () => setTimeout(startTracking, 500));

    // ---------------- RUN ----------------
    initSession();

    window.Trackly = { track };
})();

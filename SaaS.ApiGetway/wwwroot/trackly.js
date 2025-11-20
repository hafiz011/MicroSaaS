(function () {
    'use strict';

    // ===================== CONFIG =====================
    const script = document.currentScript;
    const params = new URLSearchParams(script.src.split('?')[1]);
    const API_KEY = params.get('key');
    if (!API_KEY) return console.error('Trackly: API key missing');

    const ENDPOINT_SESSION = 'https://apibizagent.techciph.com/Session/create';
    const ENDPOINT_TRACK   = 'https://apibizagent.techciph.com/Session/track';

    const DEDUPE_WINDOW = 3000;
    const seenMap = new Map();
    const oneTimeSet = new Set();
    let sessionId = localStorage.getItem('trk_sess');
    let ready = false;
    const queue = [];

    // ===================== CURRENCY AUTO DETECT =====================
    const CURRENCY_SYMBOLS = {
        '$': 'USD', 'USD': 'USD', 'US$': 'USD',
        '৳': 'BDT', 'BDT': 'BDT', 'Taka': 'BDT',
        '₹': 'INR', 'INR': 'INR', 'Rs': 'INR',
        '€': 'EUR', 'EURO': 'EUR',
        '£': 'GBP', 'GBP': 'GBP',
        '¥': 'JPY', 'JPY': 'JPY', 'CNY': 'CNY',
        '₽': 'RUB', 'RUB': 'RUB',
        '₩': 'KRW', 'KRW': 'KRW',
        'RM': 'MYR', 'MYR': 'MYR',
        'R$': 'BRL', 'SGD': 'SGD', 'CAD': 'CAD', 'AUD': 'AUD'
    };

    let detectedCurrency = null;

    function detectCurrency() {
        if (detectedCurrency) return detectedCurrency;

        // 1. From price text in DOM (most accurate)
        const priceTexts = document.body.innerText.match(/[\$£€¥৳₹₽₩]\s?[\d.,]+/g) || [];
        for (const text of priceTexts) {
            const symbol = text.replace(/[\d.,\s]/g, '').trim();
            if (CURRENCY_SYMBOLS[symbol]) {
                return CURRENCY_SYMBOLS[symbol];
            }
        }

        // 2. From meta, JSON-LD, Shopify money format
        const selectors = [
            'meta[name="currency"]',
            'meta[property="currency"]',
            'script[type="application/ld+json"]',
            '[data-currency]',
            '.money',
            'script:contains("money_format")',
            'script:contains("currencyCode")'
        ];

        for (const sel of document.querySelectorAll(selectors.join(', '))) {
            const content = sel.content || sel.textContent || sel.innerText;
            if (!content) continue;
            const found = content.match(/(USD|BDT|INR|EUR|GBP|JPY|CNY|RUB|KRW|MYR|BRL|SGD|CAD|AUD)/i);
            if (found) return found[0].toUpperCase();
        }

        // 3. From HTML lang or domain
        const lang = document.documentElement.lang || '';
        const domain = location.hostname;

        if (lang.includes('bd') || domain.includes('.bd')) return 'BDT';
        if (lang.includes('in') || domain.includes('.in')) return 'INR';
        if (domain.includes('.us') || domain.endsWith('.com')) return 'USD';
        if (domain.includes('.ca')) return 'CAD';
        if (domain.includes('.au')) return 'AUD';
        if (domain.includes('.uk') || domain.includes('.co.uk')) return 'GBP';
        if (domain.includes('.eu') || domain.includes('.de') || domain.includes('.fr')) return 'EUR';

        // Final fallback
        return 'USD';
    }

    // ===================== HELPERS =====================
    const toNum = (v) => v ? parseFloat(String(v).replace(/[^0-9.,]/g, '').replace(',','.')) || null : null;
    const now = () => Date.now();
    const dedupeKey = (event, data) => `${event}||${data?.id || data?.transaction_id || data?.name || ''}`;

    function send(payload) {
        if (!ready) return queue.push(payload);

        const key = dedupeKey(payload.event, payload.data);
        if (seenMap.has(key) && now() - seenMap.get(key) < DEDUPE_WINDOW) return;
        seenMap.set(key, now());

        navigator.sendBeacon?.(ENDPOINT_TRACK, JSON.stringify({
            sessionId, event: payload.event, data: payload.data,
            url: location.href, ts: new Date().toISOString()
        })) || fetch(ENDPOINT_TRACK, {
            method: 'POST', keepalive: true,
            headers: { 'Content-Type': 'application/json', 'X-API-KEY': API_KEY },
            body: JSON.stringify({ sessionId, event: payload.event, data: payload.data, url: location.href, ReferrerUrl: document.referrer || null, ts: new Date().toISOString() })
        });
    }

    function track(event, data = {}) {
        const onceKey = `${event}::${location.pathname}`;
        if (oneTimeSet.has(onceKey)) return;
        if (['page_view','view_item_list','begin_checkout','purchase'].includes(event)) oneTimeSet.add(onceKey);
        send({ event, data });
    }

    // ===================== SESSION =====================
    async function initSession() {
        try {
            const res = await fetch(ENDPOINT_SESSION, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-API-KEY': API_KEY },
                body: JSON.stringify({
                    Ip: "",
                    ReferrerUrl: document.referrer || null,
                    UserAgent: navigator.userAgent,
                    Language: navigator.language,
                    Screen: `${screen.width}x${screen.height}`
                })
            });
            const json = await res.json();
            if (json?.sessionId) {
                sessionId = json.sessionId;
                localStorage.setItem('trk_sess', sessionId);
                ready = true;
                while (queue.length) send(queue.shift());
            }
        } catch (e) { console.warn('Trackly session fail', e); }
    }

    // ===================== CATEGORY DETECT =====================
    function getCategory() {
        // JSON-LD + Breadcrumb + Meta (same as before)
        for (const s of document.querySelectorAll('script[type="application/ld+json"]')) {
            try {
                const data = JSON.parse(s.textContent);
                if (data['@type'] === 'Product' && data.category) return data.category;
                if (data.breadcrumbs?.length) return data.breadcrumbs.at(-1).name;
            } catch {}
        }
        const bc = document.querySelector('.breadcrumb li:last-child, .breadcrumbs li:last-child, [aria-current="page"]');
        if (bc?.textContent.trim()) return bc.textContent.trim();
        const meta = document.querySelector('meta[property="product:category"], meta[name="category"]');
        if (meta?.content) return meta.content;
        const parts = location.pathname.split('/').filter(p => p);
        return parts.length > 1 ? parts[parts.length-2] : 'Unknown';
    }

    // ===================== PRODUCT EXTRACT (with auto currency) =====================
    const processed = new WeakSet();
    const CARD_SELECTORS = '[data-product-id],[data-id],[data-sku],.product-card,.product-item,.grid-item,.card,.boost-pfs-product-item';

    function extractProduct(el) {
        if (!el || processed.has(el)) return null;
        processed.add(el);

        const container = el.closest(CARD_SELECTORS) || el;

        const id = container.dataset.productId || container.dataset.id || container.dataset.sku ||
                   container.querySelector('[data-product-id]')?.dataset.productId;

        const nameEl = container.querySelector('h1,h2,h3,.product-title,.title,a');
        const name = nameEl?.innerText.trim() || nameEl?.alt || container.getAttribute('aria-label');

        const priceEl = container.querySelector('.price,.product-price,[class*="price"],money,.amount');
        const priceText = priceEl ? (priceEl.innerText || priceEl.textContent) : null;
        const price = priceText ? toNum(priceText) : null;

        const img = container.querySelector('img');
        const image = img?.src || img?.dataset.src || null;

        if (!name && !price && !id) return null;

        let qty = 1;
        const qtyInput = container.querySelector('input[name="quantity"],input.qty') || document.querySelector('input[name="quantity"]');
        if (qtyInput) qty = parseInt(qtyInput.value || qtyInput.dataset.value || '1', 10) || 1;

        // Variant
        const variant = {};
        const form = container.closest('form') || document;
        form.querySelectorAll('select[name*="option"],select[name*="Size"],select[name*="Color"]').forEach(s => {
            const val = s.options[s.selectedIndex]?.text?.trim() || s.value;
            if (val && val !== 'Default Title') {
                const key = s.name.toLowerCase().includes('size') ? 'size' :
                            s.name.toLowerCase().includes('color') ? 'color' : s.name.split(/[[\]]/).pop();
                variant[key] = val;
            }
        });

        const currency = detectCurrency();  // Auto detect every time

        return {
            id: id || `gen_${Date.now()}_${Math.random().toString(36).slice(2,8)}`,
            name: name?.slice(0,250),
            price,
            currency,
            category: getCategory(),
            quantity: qty,
            variant: Object.keys(variant).length ? variant : null,
            image
        };
    }

    // ===================== BIND & TRACK (same as before) =====================
    function processContainer(root = document) {
        root.querySelectorAll(CARD_SELECTORS).forEach(card => {
            const product = extractProduct(card);
            if (!product) return;

            if (card.closest('.template-product, .product-single, #product, .product-page')) {
                track('view_item', product);
            }

            card.querySelectorAll('button,a,input[type="submit"],[type="button"]').forEach(btn => {
                const txt = (btn.innerText || btn.value || btn.textContent || '').toLowerCase();
                if (/add.+cart|add.+basket|add.+bag|cart|add to cart/i.test(txt) && !/checkout|buy now/i.test(txt)) {
                    btn.onclick = () => {
                        const latestQty = document.querySelector('input[name="quantity"]')?.value || 1;
                        product.quantity = parseInt(latestQty) || 1;
                        product.currency = detectCurrency(); // re-detect on click
                        track('add_to_cart', product);
                    };
                }
            });
        });
    }

    function trackList() {
        const lists = document.querySelectorAll('.products,.product-grid,.collection,.search-results,.boost-pfs-filter-products');
        lists.forEach(list => {
            const items = [...list.querySelectorAll(CARD_SELECTORS)]
                .map(extractProduct)
                .filter(Boolean)
                .slice(0,50);
            if (items.length > 2) {
                items.forEach(i => i.currency = detectCurrency());
                track('view_item_list', { items, list_name: list.className || 'collection' });
            }
        });
    }

    // Auto checkout & purchase
    if (/cart|checkout|order|payment/i.test(location.pathname)) {
        const items = [...document.querySelectorAll('.cart-item,.line-item,tr[data-id],.cart-line')]
            .map(extractProduct).filter(Boolean);
        items.forEach(i => i.currency = detectCurrency());
        if (items.length) track('begin_checkout', { items });
    }

    if (/thank|success|confirmation|order|complete/i.test(location.pathname)) {
        const orderId = document.querySelector('.order-number,#order_id,[data-order-id],.order-id')?.innerText?.trim() || `txn_${now()}`;
        const totalEl = document.querySelector('.order-total,.grand-total,.payment-due__price,.totals__total-value');
        const total = totalEl ? toNum(totalEl.innerText) : null;
        const items = [...document.querySelectorAll('.order-item,.line-item,.order-line')]
            .map(extractProduct).filter(Boolean);
        items.forEach(i => i.currency = detectCurrency());
        track('purchase', { 
            transaction_id: orderId, 
            value: total, 
            currency: detectCurrency(), 
            items 
        });
    }

    // ===================== OBSERVER & SPA =====================
    const observer = new MutationObserver(muts => {
        for (const m of muts) {
            m.addedNodes.forEach(node => {
                if (node.nodeType === 1) {
                    processContainer(node);
                    trackList();
                }
            });
        }
    });
    observer.observe(document.body, { childList: true, subtree: true });

    let oldPush = history.pushState;
    history.pushState = function () {
        oldPush.apply(this, arguments);
        setTimeout(() => {
            track('page_view', { title: document.title });
            processContainer();
            trackList();
        }, 400);
    };
    window.addEventListener('popstate', () => setTimeout(() => {
        track('page_view', { title: document.title });
        processContainer();
        trackList();
    }, 400));

    // ===================== INIT =====================
    track('page_view', { title: document.title, currency: detectCurrency() });

    if (!sessionId) initSession().then(() => { processContainer(); trackList(); });
    else { ready = true; processContainer(); trackList(); }

    // Public API
    window.Trackly = { 
        track, 
        getCurrency: detectCurrency,
        _debug: { seenMap, oneTimeSet, queue } 
    };

})();
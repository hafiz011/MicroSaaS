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

    // ===================== AUTO CURRENCY DETECTION (100% NO ERROR) =====================
    let detectedCurrency = null;

    function detectCurrency() {
        if (detectedCurrency) return detectedCurrency;

        // 1. From visible price symbols (most reliable)
        const priceRegex = /[\$£€¥৳₹₽₩]\s*[\d.,]+/g;
        const matches = document.body.innerText.match(priceRegex) || [];
        const symbolMap = { '$': 'USD', '£': 'GBP', '€': 'EUR', '¥': 'JPY', '৳': 'BDT', '₹': 'INR', '₽': 'RUB', '₩': 'KRW' };
        for (const m of matches) {
            const sym = m.replace(/[\d.,\s]/g, '').trim();
            if (symbolMap[sym]) {
                detectedCurrency = symbolMap[sym];
                return detectedCurrency;
            }
        }

        // 2. From meta tags
        const metaCurrency = document.querySelector('meta[name="currency" i], meta[property="og:price:currency" i], meta[property="product:price:currency" i]');
        if (metaCurrency?.content) {
            detectedCurrency = metaCurrency.content.trim().toUpperCase();
            return detectedCurrency;
        }

        // 3. From JSON-LD or script tags (manual text search - no :contains)
        const scripts = document.querySelectorAll('script[type="application/ld+json"], script');
        for (const s of scripts) {
            const text = s.textContent || s.innerText || '';
            if (!text) continue;

            // Shopify format
            if (text.includes('money_format') || text.includes('currencyCode')) {
                const match = text.match(/"currencyCode"\s*:\s*"([^"]+)"/i) || text.match(/money_format[^}]+"([A-Z]{3})"/i);
                if (match) {
                    detectedCurrency = match[1].toUpperCase();
                    return detectedCurrency;
                }
            }

            // Standard JSON-LD priceCurrency
            const pc = text.match(/"priceCurrency"\s*:\s*"([A-Z]{3})"/i);
            if (pc) {
                detectedCurrency = pc[1].toUpperCase();
                return detectedCurrency;
            }
        }

        // 4. From data attributes
        const dataEl = document.querySelector('[data-currency], [data-shop-currency]');
        if (dataEl?.dataset.currency || dataEl?.dataset.shopCurrency) {
            detectedCurrency = (dataEl.dataset.currency || dataEl.dataset.shopCurrency).toUpperCase();
            return detectedCurrency;
        }

        // 5. Domain + lang fallback
        const host = location.hostname.toLowerCase();
        const lang = (document.documentElement.lang || '').toLowerCase();

        if (host.includes('.bd') || lang.includes('bd')) return detectedCurrency = 'BDT';
        if (host.includes('.in') || lang.includes('in')) return detectedCurrency = 'INR';
        if (host.includes('.us') || host.endsWith('.com')) return detectedCurrency = 'USD';
        if (host.includes('.ca')) return detectedCurrency = 'CAD';
        if (host.includes('.au')) return detectedCurrency = 'AUD';
        if (host.includes('.uk') || host.includes('.co.uk')) return detectedCurrency = 'GBP';
        if (host.includes('.eu') || host.includes('.de') || host.includes('.fr')) return detectedCurrency = 'EUR';

        return detectedCurrency = 'USD'; // final fallback
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
        for (const s of document.querySelectorAll('script[type="application/ld+json"]')) {
            try {
                const data = JSON.parse(s.textContent);
                if (data['@type'] === 'Product' && data.category) return data.category;
                if (data.breadcrumbs?.length) return data.breadcrumbs.at(-1).name;
            } catch { }
        }
        const bc = document.querySelector('.breadcrumb li:last-child, .breadcrumbs li:last-child, [aria-current="page"]');
        if (bc?.textContent?.trim()) return bc.textContent.trim();
        const meta = document.querySelector('meta[property="product:category"], meta[name="category"]');
        if (meta?.content) return meta.content;
        const parts = location.pathname.split('/').filter(p => p);
        return parts.length > 1 ? parts[parts.length - 2] : 'Unknown';
    }

    // ===================== PRODUCT EXTRACT (with auto currency) =====================
    const processed = new WeakSet();
    const CARD_SELECTORS = '[data-product-id],[data-id],[data-sku],.product-card,.product-item,.grid-item,.card,.boost-pfs-product-item';

    function extractProduct(el) {
        if (!el || processed.has(el)) return null;
        processed.add(el);

        const container = el.closest(CARD_SELECTORS) || el;
        const id = container.dataset.productId || container.dataset.id || container.dataset.sku || container.querySelector('[data-product-id]')?.dataset.productId;
        const nameEl = container.querySelector('h1,h2,h3,.product-title,.title,a');
        const name = nameEl?.innerText.trim() || nameEl?.alt || container.getAttribute('aria-label');
        const priceEl = container.querySelector('.price,.product-price,[class*="price"],money,.amount');
        const price = priceEl ? toNum(priceEl.innerText || priceEl.textContent) : null;
        const img = container.querySelector('img');
        const image = img?.src || img?.dataset.src || null;

        if (!name && !price && !id) return null;

        let qty = 1;
        const qtyInput = container.querySelector('input[name="quantity"],input.qty') || document.querySelector('input[name="quantity"]');
        if (qtyInput) qty = parseInt(qtyInput.value || qtyInput.dataset.value || '1', 10) || 1;

        const variant = {};
        const form = container.closest('form') || document;
        form.querySelectorAll('select[name*="option"],select[name*="Size"],select[name*="Color"]').forEach(s => {
            const val = s.options[s.selectedIndex]?.text?.trim() || s.value;
            if (val && val !== 'Default Title') {
                const key = s.name.toLowerCase().includes('size') ? 'size' : s.name.toLowerCase().includes('color') ? 'color' : s.name.split(/[[\]]/).pop();
                variant[key] = val;
            }
        });

        return {
            id: id || `gen_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
            name: name?.slice(0, 250),
            price,
            currency: detectCurrency(),
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
        for (const m of muts) m.addedNodes.forEach(node => node.nodeType === 1 && (processContainer(node), trackList()));
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
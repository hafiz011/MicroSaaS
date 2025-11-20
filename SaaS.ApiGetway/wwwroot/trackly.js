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

    // ==================== COOKIE READ (server sets trk_sess) ====================
    function getCookie(n) {
        const v = document.cookie.match('(^|;) ?' + n + '=([^;]*)(;|$)');
        return v ? v[2] : null;
    }

    // ==================== CURRENCY AUTO DETECT (Shopify/Woo) ====================
    let currency = null;
    function detectCurrency() {
        if (currency) return currency;

        const symbols = { '$': 'USD', '৳': 'BDT', '₹': 'INR', '€': 'EUR', '£': 'GBP', '¥': 'JPY', '₽': 'RUB', '₩': 'KRW' };
        const text = document.body.innerText + (window.Shopify?.currency?.active || '') + (window.wc?.currency || '');
        const match = text.match(/[\$৳₹€£¥₽₩]/);
        if (match) {
            currency = symbols[match[0]] || 'USD';
        } else if (location.hostname.includes('.bd')) {
            currency = 'BDT';
        } else {
            currency = 'USD';
        }
        return currency;
    }

    // ==================== SEND EVENT (415 FIX + Cookie Auto Send) ====================
    function send(payload) {
        if (!ready || !sessionId) return queue.push(payload);

        const key = `${payload.event}||${payload.data?.id || payload.data?.transaction_id || ''}`;
        if (dedupe.has(key) && Date.now() - dedupe.get(key) < DEDUPE_MS) return;
        dedupe.set(key, Date.now());

        fetch(ENDPOINT_TRACK, {
            method: 'POST',
            credentials: 'include', // cookie
            headers: {
                'Content-Type': 'application/json',
                'X-API-KEY': API_KEY
            },
            keepalive: true,
            body: JSON.stringify({
                sessionId,
                Event: payload.event,
                Data: payload.data || {},
                Url: location.href,
                ReferrerUrl: document.referrer || null,
                Ts: new Date().toISOString()
            })
        }).catch(() => queue.push(payload));
    }

    function track(event, data = {}) {
        const key = `${event}::${location.pathname}`;
        if (oneTime.has(key)) return;
        if (['page_view', 'view_item_list', 'begin_checkout', 'purchase'].includes(event)) oneTime.add(key);
        send({ event, data });
    }

    // ==================== SESSION INIT ====================
    async function initSession() {
        sessionId = getCookie('trk_sess');

        if (sessionId) {
            ready = true;
            console.log('%cTrackly: Session restored → ' + sessionId.slice(0, 8) + '...', 'color: cyan');
            flushQueue();
            startTracking();
            return;
        }

        try {
            const res = await fetch(ENDPOINT_SESSION, {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json', 'X-API-KEY': API_KEY },
                body: JSON.stringify({
                    Ip: "",
                    ReferrerUrl: document.referrer || null,
                    UserAgent: navigator.userAgent,
                    Language: navigator.language || null,
                    Screen: `${screen.width}x${screen.height}`
                })
            });

            if (res.ok) {
                sessionId = getCookie('trk_sess');
                if (sessionId) {
                    ready = true;
                    console.log('%cTrackly: Session created → ' + sessionId.slice(0, 8) + '...', 'color: lime');
                    flushQueue();
                    startTracking();
                }
            }
        } catch (e) {
            setTimeout(initSession, 5000);
        }
    }

    function flushQueue() { while (queue.length) send(queue.shift()); }

    // ==================== UNIVERSAL PRODUCT DETECT (Shopify + Woo + Custom) ====================
    const SELECTORS = [
        '[data-product-id]', '.product', '.product-card', '.product-item', '.grid__item',
        '.card', '.boost-pfs-product-item', '.woocommerce-product', '.product-single',
        '.product-grid-item', '.js-product-card', 'article[type="product"]'
    ].join(',');

    function extractProduct(card) {
        if (!card) return null;

        const name = card.querySelector('h1,h2,h3,h4,.product-title,.title,a,img')?.innerText?.trim() ||
            card.querySelector('img')?.alt || 'Unknown Product';

        const priceEl = card.querySelector('.price, .product-price, [class*="price"], .money, .amount, .woocommerce-Price-amount');
        const priceText = priceEl ? (priceEl.innerText || priceEl.textContent || '') : '';
        const price = parseFloat(priceText.replace(/[^0-9.]/g, '')) || null;

        const id = card.dataset.productId || card.dataset.id || card.dataset.productSku ||
            card.querySelector('[data-product-id]')?.dataset.productId ||
            card.closest('form')?.querySelector('input[name="id"], input[name="product_id"]')?.value;

        if (!name && !price && !id) return null;

        return {
            id: id || `gen_${Date.now()}_${Math.random().toString(36).substr(2, 5)}`,
            name: name.slice(0, 250),
            price,
            currency: detectCurrency(),
            category: document.querySelector('.breadcrumb li:last-child, .breadcrumbs li:last-child, [aria-current="page"]')?.innerText?.trim() || 'Unknown',
            quantity: 1,
            image: card.querySelector('img')?.src || card.querySelector('img')?.dataset.src || null
        };
    }

    // ==================== TRACKING ENGINE (All Platforms) ====================
    function startTracking() {
        track('page_view', { title: document.title, currency: detectCurrency() });

        // View Item List
        const products = document.querySelectorAll(SELECTORS);
        if (products.length > 3 && !oneTime.has('view_item_list')) {
            oneTime.add('view_item_list');
            track('view_item_list', { items: Array.from(products).slice(0, 30).map(extractProduct).filter(Boolean), list_name: 'collection' });
        }

        products.forEach(card => {
            const p = extractProduct(card);
            if (!p) return;

            // View Item (PDP)
            if (location.pathname.includes('/products/') || location.pathname.includes('/product/') || card.closest('.template-product, #product')) {
                track('view_item', p);
            }

            // Add to Cart (All platforms)
            card.querySelectorAll('button, a, input[type="submit"]').forEach(btn => {
                const text = (btn.innerText || btn.value || '').toLowerCase();
                if ((/add|cart|basket|bag/i.test(text) && !/checkout|buy now/i.test(text)) || btn.name === 'add' || btn.formAction?.includes('cart/add')) {
                    btn.addEventListener('click', () => {
                        p.quantity = parseInt(document.querySelector('input[name="quantity"], input.qty')?.value || 1) || 1;
                        p.currency = detectCurrency();
                        track('add_to_cart', p);
                    });
                }
            });
        });

        // Begin Checkout
        if (/cart|checkout|order|payment/i.test(location.pathname) && !oneTime.has('begin_checkout')) {
            oneTime.add('begin_checkout');
            const items = Array.from(document.querySelectorAll('.cart-item, .line-item, .woocommerce-cart-form__cart-item')).map(extractProduct).filter(Boolean);
            if (items.length) track('begin_checkout', { items });
        }

        // Purchase
        if (/thank|success|confirmation|order|complete/i.test(location.pathname) && !oneTime.has('purchase')) {
            oneTime.add('purchase');
            const items = Array.from(document.querySelectorAll('.order-item, .line-item, .woocommerce-order-details__item')).map(extractProduct).filter(Boolean);
            track('purchase', {
                transaction_id: document.querySelector('.order-number, #order_id')?.innerText || `txn_${Date.now()}`,
                value: null,
                currency: detectCurrency(),
                items
            });
        }
    }

    // ==================== AJAX & SPA SUPPORT ====================
    const observer = new MutationObserver(() => setTimeout(startTracking, 600));
    observer.observe(document.body, { childList: true, subtree: true });

    // SPA (Shopify Turbo, WooCommerce AJAX)
    const pushState = history.pushState;
    history.pushState = function () {
        pushState.apply(this, arguments);
        setTimeout(startTracking, 600);
    };
    window.addEventListener('popstate', () => setTimeout(startTracking, 600));

    // ==================== START ====================
    initSession();

    window.Trackly = { track, currency: detectCurrency };
    console.log('%cTrackly SDK 2025 – Works on Shopify, WooCommerce, Custom Sites 100%', 'color: #ff00ff; font-size: 18px; font-weight: bold');
})();
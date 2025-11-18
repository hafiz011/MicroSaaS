(function () {
    // Extract API Key from script tag URL
    const scriptTag = document.currentScript;
    const apiKey = new URLSearchParams(scriptTag.src.split('?')[1]).get("key");

    if (!apiKey) {
        console.error("Trackly: Missing x-api-key!");
        return;
    }

    // Helper: Save cookie
    function setCookie(name, value, days) {
        let expires = "";
        if (days) {
            const date = new Date();
            date.setTime(date.getTime() + days * 24 * 60 * 60 * 1000);
            expires = "; expires=" + date.toUTCString();
        }
        document.cookie = name + "=" + (value || "") + expires + "; path=/; Secure; SameSite=Lax";
    }

    function getCookie(name) {
        const nameEQ = name + "=";
        const ca = document.cookie.split(";");
        for (let i = 0; i < ca.length; i++) {
            let c = ca[i];
            while (c.charAt(0) === " ") c = c.substring(1, c.length);
            if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length, c.length);
        }
        return null;
    }

    // Start session if not exists
    let sessionId = getCookie("trackly_session");

    function startSession() {
        const payload = {
            Ip: "",
            ReferrerUrl: document.referrer,
            UserAgent: navigator.userAgent,
            Language: navigator.language || navigator.userLanguage,
            Screen: `${window.screen.width}x${window.screen.height}`
        };

        return fetch("https://apibizagent.techciph.com/Session/create", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "X-API-KEY": apiKey
            },
            body: JSON.stringify(payload)
        })
            .then(res => res.json())
            .then(result => {
                if (result.sessionId) {
                    sessionId = result.sessionId;
                    setCookie("trackly_session", sessionId, 1);
                    console.log("Trackly session started:", sessionId);
                    return sessionId;
                }
            })
            .catch(err => console.error("Trackly init failed:", err));
    }

    // Event tracking function
    window.Trackly = {
        track: function (eventName, data = {}) {
            if (!sessionId) return;

            fetch("https://apibizagent.techciph.com/Session/track", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-API-KEY": apiKey
                },
                body: JSON.stringify({
                    sessionId,
                    event: eventName,
                    data,
                    url: window.location.href,
                    ReferrerUrl: document.referrer,
                    ts: new Date().toISOString()
                })
            }).catch(err => console.error("Trackly event failed:", err));
        }
    };

    // AUTO EVENT TRACKING
    function enableAutoTracking() {

        // 1️ PAGE VIEW
        window.Trackly.track("page_view", {
            title: document.title
        });

        // 2️ LINK CLICK
        document.addEventListener("click", function (e) {
            let el = e.target.closest("a");
            if (!el) return;

            Trackly.track("link_click", {
                text: el.innerText,
                href: el.href
            });
        });

        // 3️ BUTTON CLICK
        document.addEventListener("click", function (e) {
            let el = e.target.closest("button");
            if (!el) return;

            Trackly.track("button_click", {
                text: el.innerText || el.value
            });
        });

        // 4️ FORM SUBMIT
        document.addEventListener("submit", function (e) {
            let form = e.target;
            Trackly.track("form_submit", {
                action: form.action,
                id: form.id,
                classes: form.className
            });
        });

        // 5️ SCROLL DEPTH
        let maxScroll = 0;
        window.addEventListener("scroll", function () {
            let current = Math.round(
                (window.scrollY / (document.body.scrollHeight - window.innerHeight)) * 100
            );

            if (current > maxScroll) {
                maxScroll = current;
                Trackly.track("scroll_depth", { depth: maxScroll });
            }
        });
    }

    // Initialize
    if (!sessionId) {
        startSession().then(() => enableAutoTracking());
    } else {
        enableAutoTracking();
    }

})();

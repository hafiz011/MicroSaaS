
(function () {
    const tenantKey = new URLSearchParams(window.location.search).get("id");

    // Collect client-side data
    const data = {
        referrer: document.referrer || null,
        userAgent: navigator.userAgent,
        screen: `${window.screen.width}x${window.screen.height}`,
        url: window.location.href,
    };

    // Send to backend
    fetch("https://api.mydomain.com/api/session/start", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
            tenantKey,
            ...data
        })
    })
        .then(res => res.json())
        .then(result => {
            if (result.sessionId) {
                // Save session ID in cookie
                document.cookie = `trackly_session=${result.sessionId}; path=/; Secure; SameSite=Lax`;
            }
        })
        .catch(err => {
            console.error("Trackly session start failed", err);
        });
})();






//(function (window) {
//    function getScreenResolution() {
//        return `${window.screen.width}x${window.screen.height}`;
//    }

//    // Bootstrap session
//    async function startSession(apiKey) {
//        try {
//            await fetch("https://api.yoursaas.com/session/start", {
//                method: "POST",
//                headers: {
//                    "Content-Type": "application/json",
//                    "x-api-key": apiKey
//                },
//                body: JSON.stringify({
//                    referrer: document.referrer || null,
//                    userAgent: navigator.userAgent,
//                    screenResolution: getScreenResolution()
//                })
//            });
//        } catch (err) {
//            console.error("Session start failed:", err);
//        }
//    }

//    // Global function
//    window.trackly = async function (command, name, params) {
//        if (command === "init") {
//            window._tracklyApiKey = name;
//            await startSession(name); // ✅ প্রথমেই session create করবে

//            // Auto send first pageview
//            sendEvent("page_view", { url: window.location.href });
//        }

//        if (command === "event") {
//            sendEvent(name, params);
//        }
//    };

//    function getCookie(name) {
//        return document.cookie.split("; ").reduce((r, v) => {
//            const parts = v.split("=");
//            return parts[0] === name ? decodeURIComponent(parts[1]) : r;
//        }, "");
//    }

//    function sendEvent(eventName, data) {
//        const sessionId = getCookie("_trackly_sid"); // ✅ backend থেকে দেওয়া sessionId
//        if (!sessionId) return; // যদি session না থাকে, skip করবে

//        const payload = {
//            event: eventName,
//            data: data || {},
//            url: window.location.href,
//            referrer: document.referrer || null,
//            sessionId: sessionId,
//            userAgent: navigator.userAgent,
//            screenResolution: getScreenResolution(),
//            timestamp: new Date().toISOString()
//        };

//        fetch("https://api.yoursaas.com/collect", {
//            method: "POST",
//            headers: {
//                "Content-Type": "application/json",
//                "x-api-key": window._tracklyApiKey
//            },
//            body: JSON.stringify(payload)
//        }).catch(err => console.error("Trackly event failed:", err));
//    }
//})(window);

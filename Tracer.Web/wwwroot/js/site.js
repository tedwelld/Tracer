const promptShell = document.getElementById("alertPrompt");
const promptTitle = document.getElementById("promptTitle");
const promptMessage = document.getElementById("promptMessage");
const acknowledgeButton = document.getElementById("promptAcknowledge");
const trustButton = document.getElementById("promptTrust");

const seenAlerts = new Set();
let currentPrompt = null;

document.querySelectorAll("[data-ack-alert]").forEach((button) => {
    button.addEventListener("click", async () => {
        await postJson(`/api/alerts/${button.dataset.ackAlert}/acknowledge`);
        window.location.reload();
    });
});

document.querySelectorAll("[data-know-device]").forEach((button) => {
    button.addEventListener("click", async () => {
        await postJson(`/api/devices/${button.dataset.knowDevice}/known`);
        window.location.reload();
    });
});

if (typeof Notification !== "undefined" && Notification.permission === "default") {
    Notification.requestPermission().catch(() => undefined);
}

if (promptShell && promptTitle && promptMessage && acknowledgeButton && trustButton) {
    acknowledgeButton.addEventListener("click", async () => {
        if (!currentPrompt) {
            return;
        }

        await postJson(`/api/alerts/${currentPrompt.id}/acknowledge`);
        hidePrompt();
        window.location.reload();
    });

    trustButton.addEventListener("click", async () => {
        if (!currentPrompt) {
            return;
        }

        await postJson(`/api/devices/${currentPrompt.deviceId}/known`);
        hidePrompt();
        window.location.reload();
    });

    refreshPendingAlerts();
    window.setInterval(refreshPendingAlerts, 15000);
}

async function refreshPendingAlerts() {
    const response = await fetch("/api/alerts/pending", {
        headers: { Accept: "application/json" }
    });

    if (!response.ok) {
        return;
    }

    const alerts = await response.json();
    const nextPrompt = alerts.find((alert) => !seenAlerts.has(alert.id));

    if (!nextPrompt) {
        return;
    }

    seenAlerts.add(nextPrompt.id);
    currentPrompt = nextPrompt;

    promptTitle.textContent = nextPrompt.title;
    promptMessage.textContent = `${nextPrompt.deviceLabel} (${nextPrompt.radioKind}) ${nextPrompt.message}`;
    promptShell.hidden = false;

    if (typeof Notification !== "undefined" && Notification.permission === "granted") {
        new Notification(nextPrompt.title, {
            body: `${nextPrompt.deviceLabel} (${nextPrompt.radioKind})`
        });
    }
}

function hidePrompt() {
    currentPrompt = null;
    promptShell.hidden = true;
}

async function postJson(url) {
    await fetch(url, {
        method: "POST",
        headers: {
            Accept: "application/json"
        }
    });
}

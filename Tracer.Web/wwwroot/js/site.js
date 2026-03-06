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

async function postJson(url) {
    await fetch(url, {
        method: "POST",
        headers: {
            Accept: "application/json"
        }
    });
}

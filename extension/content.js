// Content script: solo escucha peticiones del background (icono de la barra)
// para disparar el protocolo mediafy:// desde la propia página vía anchor link.
// SIN botón flotante: la entrada es por clic derecho o el icono.
(function () {
  function launchMediaFy(url) {
    const proto = "mediafy://download?url=" + encodeURIComponent(url);
    const a = document.createElement("a");
    a.href = proto;
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    setTimeout(() => a.remove(), 100);
  }

  try {
    chrome.runtime.onMessage.addListener((msg) => {
      if (msg && msg.type === "mediafy-launch" && msg.url) launchMediaFy(msg.url);
    });
  } catch (e) {}
})();

// Background: para clic en icono y menú contextual, le pide al content script
// de la pestaña activa que dispare el protocolo desde la propia página.
// (Mucho más confiable que abrir una pestaña con el protocolo).

function launchInTab(tabId, url) {
  if (tabId == null) return;
  chrome.tabs.sendMessage(tabId, { type: "mediafy-launch", url }, () => {
    // Si el content script no está cargado (pestaña no soportada), lo inyectamos al vuelo
    if (chrome.runtime.lastError) {
      chrome.scripting.executeScript({
        target: { tabId },
        func: (u) => {
          const proto = "mediafy://download?url=" + encodeURIComponent(u);
          const a = document.createElement("a");
          a.href = proto; a.style.display = "none";
          document.body.appendChild(a); a.click();
          setTimeout(() => a.remove(), 100);
        },
        args: [url]
      }).catch(() => {});
    }
  });
}

chrome.action.onClicked.addListener((tab) => {
  if (tab && tab.url) launchInTab(tab.id, tab.url);
});

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "mediafy-send-page",
    title: "Descargar esta página con MediaFy",
    contexts: ["page", "video", "audio"]
  });
  chrome.contextMenus.create({
    id: "mediafy-send-link",
    title: "Descargar este enlace con MediaFy",
    contexts: ["link"]
  });
});

chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (!tab) return;
  const url = info.menuItemId === "mediafy-send-link" ? info.linkUrl : tab.url;
  if (url) launchInTab(tab.id, url);
});

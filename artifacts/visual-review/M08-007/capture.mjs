import { writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const cdpOrigin = "http://127.0.0.1:9227";
const targetUrl = "http://127.0.0.1:3310/";
const outputDirectory = dirname(fileURLToPath(import.meta.url));

const target = await fetch(`${cdpOrigin}/json/new?${encodeURIComponent(targetUrl)}`, {
  method: "PUT",
}).then((response) => response.json());

const socket = new WebSocket(target.webSocketDebuggerUrl);
const pending = new Map();
const eventWaiters = new Map();
const runtimeIssues = [];
let nextId = 1;

socket.addEventListener("message", ({ data }) => {
  const message = JSON.parse(data);
  if (message.id && pending.has(message.id)) {
    const { resolve, reject } = pending.get(message.id);
    pending.delete(message.id);
    if (message.error) reject(new Error(message.error.message));
    else resolve(message.result);
    return;
  }

  if (message.method === "Runtime.exceptionThrown") {
    runtimeIssues.push(message.params.exceptionDetails.text);
  }
  if (message.method === "Log.entryAdded" && message.params.entry.level === "error") {
    runtimeIssues.push(message.params.entry.text);
  }

  const waiters = eventWaiters.get(message.method);
  if (waiters?.length) waiters.shift()(message.params);
});

await new Promise((resolve, reject) => {
  socket.addEventListener("open", resolve, { once: true });
  socket.addEventListener("error", reject, { once: true });
});

function command(method, params = {}) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
}

function nextEvent(method) {
  return new Promise((resolve) => {
    const waiters = eventWaiters.get(method) ?? [];
    waiters.push(resolve);
    eventWaiters.set(method, waiters);
  });
}

async function setViewport(width, height) {
  await command("Emulation.setDeviceMetricsOverride", {
    width,
    height,
    deviceScaleFactor: 1,
    mobile: false,
  });
  const loaded = nextEvent("Page.loadEventFired");
  await command("Page.navigate", { url: targetUrl });
  await loaded;
  await new Promise((resolve) => setTimeout(resolve, 250));
}

async function evaluate(expression) {
  const result = await command("Runtime.evaluate", {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  return result.result.value;
}

async function screenshot(name, width, height, captureBeyondViewport = false) {
  const result = await command("Page.captureScreenshot", {
    format: "png",
    fromSurface: true,
    captureBeyondViewport,
    clip: { x: 0, y: 0, width, height, scale: 1 },
  });
  await writeFile(join(outputDirectory, name), Buffer.from(result.data, "base64"));
}

await command("Page.enable");
await command("Runtime.enable");
await command("Log.enable");

const viewports = [];
for (const width of [1440, 1280, 1024, 768, 390, 360]) {
  const height = width === 390 ? 844 : 1000;
  await setViewport(width, height);
  const metrics = await evaluate(`(() => {
    const grid = document.querySelector('[class*="featureGrid"]');
    const details = document.querySelector('header details');
    return {
      width: ${width},
      scrollWidth: document.documentElement.scrollWidth,
      featureColumns: grid ? getComputedStyle(grid).gridTemplateColumns.split(' ').length : 0,
      mobileMenu: details ? getComputedStyle(details).display !== 'none' : false,
    };
  })()`);
  viewports.push(metrics);
}

await setViewport(1440, 1000);
await screenshot("landing-desktop.png", 1440, 1000);
await screenshot("landing-desktop-full.png", 1440, 7200, true);

await setViewport(390, 844);
await screenshot("landing-mobile.png", 390, 844);
const mobileHeight = await evaluate("document.documentElement.scrollHeight");
await screenshot("landing-mobile-full.png", 390, mobileHeight, true);

const mobileMenu390 = await evaluate(`(() => {
  const details = document.querySelector('header details');
  details.open = true;
  const menu = details.querySelector('nav');
  const rect = menu.getBoundingClientRect();
  return {
    open: details.open,
    left: rect.left,
    right: rect.right,
    top: rect.top,
    bottom: rect.bottom,
    insideViewport: rect.left >= 0 && rect.right <= innerWidth,
  };
})()`);
await screenshot("landing-mobile-menu.png", 390, 844);

const report = {
  route: "/",
  horizontalOverflow: viewports.some(({ width, scrollWidth }) => scrollWidth > width),
  viewports,
  mobileMenu390,
  runtimeIssues,
};
await writeFile(join(outputDirectory, "responsive-review.json"), `${JSON.stringify(report, null, 2)}\n`);

socket.close();
await fetch(`${cdpOrigin}/json/close/${target.id}`);
console.log(JSON.stringify(report, null, 2));

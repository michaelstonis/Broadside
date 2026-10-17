// Starts Blazor and holds the demo's small browser helpers. Classic script, no dependencies.
//
// The runtime folder is wherever index.html loads blazor.webassembly.js from: "_framework" normally, or the folder publishing with
// -p:BroadsideRuntimeFolder=<name> moved it to (for hosts that reserve paths starting with "_"). Blazor asks loadBootResource for every
// runtime file (dotnet.js first, then the runtime, the assemblies and the rest); a default address under "_framework/" is mapped to
// the real folder, so nothing else needs to know where the runtime lives. Every address stays relative to the page: the site works
// from any sub-path.
(function () {
  "use strict";

  const script = document.querySelector('script[src$="blazor.webassembly.js"]');
  const src = script ? script.getAttribute("src") : "_framework/blazor.webassembly.js";
  const folder = src.substring(0, src.lastIndexOf("/"));

  window.broadsideDemo = {
    runtimeFolder: folder,

    // Saves bytes as a file through a temporary object URL (no server round trip).
    download(name, bytes) {
      const url = URL.createObjectURL(new Blob([bytes], { type: "application/pdf" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = name;
      link.rel = "noopener";
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    },

    // Whether panels open as bottom sheets (phones) rather than a side drawer.
    isNarrow() {
      return window.matchMedia("(max-width: 899px)").matches;
    },

    // Scrolls an element of a panel into view (the field tapped in the page).
    reveal(selector) {
      const element = document.querySelector(selector);
      if (element) {
        element.scrollIntoView({ block: "nearest", behavior: "smooth" });
      }
    },

    // Moves the keyboard focus to an element (after a panel opens).
    focus(selector) {
      const element = document.querySelector(selector);
      if (element) {
        element.focus({ preventScroll: true });
      }
    },
  };

  const remap = (address) =>
    folder === "_framework" || typeof address !== "string" ? undefined : address.replace(/(^|\/)_framework\//, "$1" + folder + "/");

  Blazor.start({
    loadBootResource(type, name, defaultUri) {
      return remap(defaultUri);
    },
  });
})();

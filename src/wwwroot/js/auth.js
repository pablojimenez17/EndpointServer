"use strict";

/*
 * auth.js — compartido por todas las páginas protegidas.
 *
 *  · Enlaza el botón #logoutButton del sidebar con POST /api/logout.
 *  · Si una petición a la API responde 401 (sesión caducada), vuelve al login
 *    conservando la página actual en returnUrl.
 */

(function () {
  const button = document.querySelector("#logoutButton");

  if (button) {
    button.addEventListener("click", async () => {
      button.disabled = true;

      try {
        await fetch("/api/logout", { method: "POST" });
      } catch {
        // Si falla la petición se navega igualmente al login.
      }

      window.location.replace("/login.html");
    });
  }

  const originalFetch = window.fetch.bind(window);

  window.fetch = async function (input, init) {
    const response = await originalFetch(input, init);

    if (response.status === 401) {
      const url = typeof input === "string" ? input : input?.url ?? "";
      const interna = url.startsWith("/") && !url.startsWith("/api/login");

      if (interna) {
        const returnUrl = encodeURIComponent(
          window.location.pathname + window.location.search
        );
        window.location.replace(`/login.html?returnUrl=${returnUrl}`);
      }
    }

    return response;
  };
})();

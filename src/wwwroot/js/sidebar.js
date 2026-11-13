"use strict";

/*
 * sidebar.js — Indicador de estado del servidor en el pie del menú lateral.
 *
 * Compartido por todas las páginas (/, /equipos/, /Software/, /Servicios/,
 * /PowerShell/ y /Tareas/). Consulta GET /api/status cada 15 segundos y
 * pinta tres elementos del sidebar:
 *   #apiIndicator → punto de color (verde OK / rojo incidencia / ámbar cargando)
 *   #apiState     → texto "Servidor operativo" o "Servidor con incidencias"
 *   #serverVersion→ "Versión X.Y.Z" reportada por la API
 *
 * Si la página no tiene esos IDs, el script termina sin hacer nada.
 */

// IIFE: encapsula el código para no declarar variables globales.
(function () {
  const indicator = document.querySelector("#apiIndicator");
  const apiState = document.querySelector("#apiState");
  const serverVersion = document.querySelector("#serverVersion");

  // Páginas sin sidebar (o HTML parcial): salir sin error.
  if (!indicator || !apiState || !serverVersion) return;

  // Descarga /api/status y actualiza el indicador según api y database.
  async function refreshStatus() {
    try {
      const response = await fetch("/api/status", {
        cache: "no-store",
        headers: { Accept: "application/json" }
      });

      // La API responde 200/503 con JSON; parseamos aunque falle el estado.
      let payload = null;
      try {
        payload = await response.json();
      } catch {
        payload = null;
      }

      // Verde solo si la API y la base de datos responden "OK".
      const apiOk = payload?.api === "OK";
      const dbOk = payload?.database === "OK";

      indicator.className = `service-dot ${apiOk && dbOk ? "ok" : "error"}`;
      apiState.textContent = apiOk && dbOk ? "Servidor operativo" : "Servidor con incidencias";
      serverVersion.textContent = `Versión ${payload?.version || "—"}`;
    } catch {
      // Servidor caído o red inaccesible: punto rojo y versión desconocida.
      indicator.className = "service-dot error";
      apiState.textContent = "Error de conexión";
      serverVersion.textContent = "Versión —";
    }
  }

  // Primera consulta inmediata + refresco automático cada 15 segundos.
  refreshStatus();
  window.setInterval(refreshStatus, 15000);
})();

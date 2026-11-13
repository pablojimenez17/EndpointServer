"use strict";

/*
 * realtime.js — conexión SignalR compartida (hub /hub/tiemporeal).
 *
 * El servidor avisa en tiempo real cuando cambia algo. Este script se
 * conecta una sola vez y reenvía cada aviso como un evento del DOM, para
 * que cada página lo consuma sin acoplarse a SignalR:
 *
 *   evento del hub         → evento del DOM
 *   ─────────────────────    ─────────────────────────────
 *   mantenimientoCambiado  → "realtime:mantenimiento"
 *   equipoHeartbeat        → "realtime:heartbeat"
 *   equipoRegistrado       → "realtime:equipo"
 *   (tras una reconexión)  → "realtime:reconectado"
 *
 * Requiere /lib/signalr.min.js (window.signalR). Si no está o no se puede
 * conectar, no hace nada: la web sigue funcionando con su refresco normal.
 */
(function () {
  if (typeof window.signalR === "undefined") {
    console.error("[realtime] Falta /lib/signalr.min.js (window.signalR no existe).");
    return;
  }

  function emitir(nombre, detalle) {
    document.dispatchEvent(new CustomEvent(nombre, { detail: detalle }));
  }

  const conexion = new window.signalR.HubConnectionBuilder()
    .withUrl("/hub/tiemporeal")
    .withAutomaticReconnect()
    .configureLogging(window.signalR.LogLevel.Warning)
    .build();

  conexion.on("mantenimientoCambiado", (payload) => emitir("realtime:mantenimiento", payload));
  conexion.on("tareaAvanzadaCambiada", (payload) => emitir("realtime:tareaAvanzada", payload));
  conexion.on("equipoHeartbeat", (payload) => emitir("realtime:heartbeat", payload));
  conexion.on("equipoRegistrado", (payload) => emitir("realtime:equipo", payload));

  conexion.onreconnecting(() => console.warn("[realtime] conexión perdida, reconectando…"));
  // Tras reconectar puede haberse perdido algún aviso: la página recarga.
  conexion.onreconnected(() => {
    console.info("[realtime] reconectado");
    emitir("realtime:reconectado", null);
  });

  function conectar() {
    conexion.start()
      .then(() => console.info("[realtime] conectado al hub /hub/tiemporeal"))
      .catch((error) => {
        // El servidor puede estar arrancando: reintento cada 5 segundos.
        console.warn("[realtime] no se ha podido conectar; reintento en 5 s:", error);
        window.setTimeout(conectar, 5000);
      });
  }

  conectar();
})();

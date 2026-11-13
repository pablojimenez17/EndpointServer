"use strict";

/*
 * login.js — envía las credenciales a POST /api/login y, si son correctas,
 * redirige a la página solicitada (parámetro returnUrl) o al dashboard.
 */

(function () {
  const form = document.querySelector("#loginForm");
  const usuario = document.querySelector("#usuario");
  const password = document.querySelector("#password");
  const errorBox = document.querySelector("#loginError");
  const button = document.querySelector("#loginButton");

  if (!form || !usuario || !password || !errorBox || !button) return;

  const returnUrl = new URLSearchParams(window.location.search).get("returnUrl");

  form.addEventListener("submit", async (event) => {
    event.preventDefault();
    errorBox.classList.add("hidden");

    if (!usuario.value.trim() || !password.value) {
      showError("Introduce el usuario y la contraseña.");
      return;
    }

    button.disabled = true;

    try {
      const response = await fetch("/api/login", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json"
        },
        body: JSON.stringify({
          usuario: usuario.value.trim(),
          password: password.value
        })
      });

      if (!response.ok) {
        showError("Usuario o contraseña incorrectos.");
        password.value = "";
        password.focus();
        return;
      }

      window.location.replace(destinoSeguro(returnUrl));
    } catch {
      showError("No se ha podido conectar con el servidor.");
    } finally {
      button.disabled = false;
    }
  });

  function showError(message) {
    errorBox.textContent = message;
    errorBox.classList.remove("hidden");
  }

  // Solo se permiten rutas internas ("/algo"); nunca URLs absolutas o //host.
  function destinoSeguro(valor) {
    if (!valor || !valor.startsWith("/") || valor.startsWith("//")) {
      return "/";
    }

    return valor;
  }
})();

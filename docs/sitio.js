// DeskIsleBar: idioma de la pagina y link directo al ultimo instalador.
(function () {
  var raiz = document.documentElement;

  function leer() { try { return localStorage.getItem('idioma'); } catch (e) { return null; } }
  function guardar(v) { try { localStorage.setItem('idioma', v); } catch (e) { } }

  function aplicar(idioma) {
    raiz.setAttribute('data-idioma', idioma);
    raiz.setAttribute('lang', idioma);
    document.querySelectorAll('[data-cambiar]').forEach(function (b) {
      b.setAttribute('aria-pressed', String(b.getAttribute('data-cambiar') === idioma));
    });
  }

  // Elegido antes, o el del navegador (español si empieza con "es", si no inglés)
  var inicial = leer() || ((navigator.language || 'es').toLowerCase().indexOf('es') === 0 ? 'es' : 'en');
  aplicar(inicial === 'en' ? 'en' : 'es');
  document.querySelectorAll('[data-cambiar]').forEach(function (b) {
    b.addEventListener('click', function () {
      var idioma = b.getAttribute('data-cambiar');
      aplicar(idioma);
      guardar(idioma);
    });
  });

  // El boton baja directo el instalador de la ultima version (si la API no responde, queda el link a Releases)
  var boton = document.getElementById('descargar');
  if (!boton || !window.fetch) return;
  fetch('https://api.github.com/repos/FacuGhub/DeskIsleBar/releases/latest')
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (release) {
      if (!release || !release.assets) return;
      var instalador = release.assets.find(function (a) { return /^DeskIsleBar-Setup-.*\.exe$/i.test(a.name); });
      if (!instalador) return;
      boton.href = instalador.browser_download_url;
      var version = document.getElementById('version');
      if (version && release.tag_name) {
        version.querySelectorAll('span').forEach(function (s) { s.textContent = release.tag_name + ' · ' + s.textContent; });
      }
    })
    .catch(function () { });
})();

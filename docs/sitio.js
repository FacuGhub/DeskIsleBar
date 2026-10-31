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

  // Contacto: mailto no hace nada si la PC no tiene una app de correo configurada (lo comun en Windows),
  // asi que se abre un cartelito con la direccion para copiarla o escribir desde Gmail u Outlook
  var mail = 'facughub.apps@gmail.com';
  var cartel = null;
  function cerrarCartel() { if (cartel) { cartel.remove(); cartel = null; } }
  function abrirCartel(link) {
    var asunto = encodeURIComponent('DeskIsleBar');
    cartel = document.createElement('div');
    cartel.className = 'cartel-contacto';
    cartel.setAttribute('role', 'dialog');
    cartel.innerHTML = '<div class="cartel-caja">' +
      '<p class="cartel-titulo"><span class="es">Escribime a</span><span class="en">Write to me at</span></p>' +
      '<p class="cartel-mail">' + mail + '</p>' +
      '<div class="cartel-acciones">' +
        '<button type="button" class="boton" data-copiar><span class="es">Copiar</span><span class="en">Copy</span></button>' +
        '<a class="boton" target="_blank" rel="noopener" href="https://mail.google.com/mail/?view=cm&fs=1&to=' + mail + '&su=' + asunto + '">Gmail</a>' +
        '<a class="boton" target="_blank" rel="noopener" href="https://outlook.live.com/mail/0/deeplink/compose?to=' + mail + '&subject=' + asunto + '">Outlook</a>' +
        '<a class="boton" href="mailto:' + mail + '?subject=' + asunto + '"><span class="es">App de correo</span><span class="en">Mail app</span></a>' +
      '</div></div>';
    link.parentNode.insertBefore(cartel, link.nextSibling);
    // Queda abajo del pie: se lleva a la vista para no tener que scrollear
    link.parentNode.scrollIntoView({ behavior: 'smooth', block: 'end' });
    var copiar = cartel.querySelector('[data-copiar]');
    copiar.addEventListener('click', function () {
      var listo = function () {
        copiar.innerHTML = '<span class="es">¡Copiado!</span><span class="en">Copied!</span>';
      };
      if (navigator.clipboard) navigator.clipboard.writeText(mail).then(listo, function () { });
    });
  }
  document.querySelectorAll('a.contacto').forEach(function (link) {
    link.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      if (cartel) cerrarCartel(); else abrirCartel(link);
    });
  });
  document.addEventListener('click', function (e) { if (cartel && !cartel.contains(e.target)) cerrarCartel(); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') cerrarCartel(); });

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

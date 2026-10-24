// Puts the texts of DocFX's template (search box, «In this article», theme menu…) in Spanish on the pages of es/.
// DocFX has one table of those texts for the whole site, so they are changed in the built pages, after the build.
// Run by publish.ps1; by hand: node site/localize.js (after dotnet docfx site/docfx.json).
const fs = require('fs');
const path = require('path');

const es = {
  'loc:changeTheme': 'Cambiar el tema',
  'loc:copy': 'Copiar',
  'loc:downloadPdf': 'Descargar PDF',
  'loc:inThisArticle': 'En esta página',
  'loc:nextArticle': 'Siguiente',
  'loc:prevArticle': 'Anterior',
  'loc:searchNoResults': 'Nada para &quot;{query}&quot;',
  'loc:searchResultsCount': '{count} resultados para &quot;{query}&quot;',
  'loc:themeAuto': 'Automático',
  'loc:themeDark': 'Oscuro',
  'loc:themeLight': 'Claro',
  'loc:tocFilter': 'Filtrar por título',
};
const attributes = [
  ['placeholder="Search"', 'placeholder="Buscar"'],
  ['aria-label="Search"', 'aria-label="Buscar"'],
  ['aria-label="Close"', 'aria-label="Cerrar"'],
  ['aria-label="Show table of contents"', 'aria-label="Enseñar el índice"'],
  ['aria-label="Toggle navigation"', 'aria-label="Abrir o cerrar la navegación"'],
];

const root = path.join(__dirname, '_site', 'es');
let pages = 0;
(function walk(folder) {
  for (const name of fs.readdirSync(folder)) {
    const file = path.join(folder, name);
    if (fs.statSync(file).isDirectory()) { walk(file); continue; }
    if (!name.endsWith('.html')) continue;
    let html = fs.readFileSync(file, 'utf8');
    for (const [key, text] of Object.entries(es)) {
      html = html.replace(new RegExp(`(<meta name="${key}" content=")[^"]*(")`), `$1${text}$2`);
    }
    for (const [from, to] of attributes) html = html.split(from).join(to);
    fs.writeFileSync(file, html);
    pages++;
  }
})(root);
console.log(`localize: ${pages} páginas en español`);

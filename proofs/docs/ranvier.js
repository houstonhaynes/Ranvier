// Rebuilds rocqnavi's sidebar: layered file list, current page, filter, and an outline of the page.
(function () {
  var LAYERS = [
    ['Base', 'Foundations'],
    ['Reference', 'Reference semantics'],
    ['Pure', 'Pure engine'],
    ['Conformance', 'Conformance']
  ];

  function stored(key) {
    try { return window.localStorage.getItem(key); } catch (e) { return null; }
  }

  function el(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text) n.textContent = text;
    return n;
  }

  function buildFiles(side, here) {
    var root = side.querySelector('#Ranvier');
    var list = el('nav', 'nav-files');
    LAYERS.forEach(function (layer) {
      var group = root.querySelector('#' + CSS.escape('Ranvier.' + layer[0]));
      if (!group) return;
      group.querySelector('summary').textContent = layer[1];
      list.appendChild(group);
    });
    var other = el('details');
    other.id = 'Ranvier.Other';
    other.appendChild(el('summary', '', 'Build checks'));
    Array.prototype.slice.call(root.children).forEach(function (c) {
      if (c.tagName === 'A') other.appendChild(c);
    });
    list.appendChild(other);

    var onModule = !!list.querySelector('a[href="' + here + '"]');
    list.querySelectorAll('details').forEach(function (d) {
      var isHere = false;
      d.querySelectorAll('a').forEach(function (a) {
        if (a.getAttribute('href') === here) {
          a.classList.add('current');
          isHere = true;
        }
      });
      d.open = isHere || !onModule;
    });
    return list;
  }

  function theoremName(kw) {
    var n = kw.nextElementSibling;
    while (n && !(n.classList.contains('id') && n.textContent.trim())) n = n.nextElementSibling;
    return n;
  }

  function buildOutline(content) {
    var title = content.querySelector('h1.title');
    var items = [];
    var count = 0;
    content.querySelectorAll('h1, h2, h3, span.vernacular').forEach(function (n) {
      if (n === title) return;
      if (n.tagName === 'SPAN') {
        if (!/^(Theorem|Corollary)$/.test(n.textContent)) return;
        if (n.closest('details')) return;
        var name = theoremName(n);
        if (!name) return;
        var label = name.textContent.trim();
        name.id = name.id || 'thm-' + label;
        items.push({ node: name, label: label, kind: 'thm' });
      } else {
        n.id = n.id || 'sec-' + count++;
        items.push({ node: n, label: n.textContent.trim(), kind: n.tagName === 'H1' ? 'sec' : 'sub' });
      }
    });
    return items;
  }

  function init() {
    var side = document.querySelector('div.sidebar');
    var content = document.querySelector('div.content');
    if (!side || !side.querySelector('#Ranvier')) return;
    var here = location.pathname.split('/').pop() || 'index.html';

    var files = buildFiles(side, here);
    var brand = el('a', 'brand', 'Ranvier proofs');
    brand.href = 'index.html';
    var filter = el('input', 'nav-filter');
    filter.type = 'search';
    filter.placeholder = 'Filter files and theorems';
    filter.setAttribute('aria-label', 'Filter files and theorems');

    var outlineNav = el('nav', 'nav-outline');
    var links = [];
    if (content) {
      var items = buildOutline(content);
      var hasThm = items.some(function (i) { return i.kind === 'thm'; });
      if (items.length) {
        outlineNav.appendChild(el('div', 'nav-label', hasThm ? 'On this page' : 'Sections'));
        items.forEach(function (i) {
          var a = el('a', 'o-' + i.kind, i.label);
          a.href = '#' + i.node.id;
          outlineNav.appendChild(a);
          links.push({ a: a, node: i.node });
        });
      }
    }

    side.textContent = '';
    side.appendChild(brand);
    side.appendChild(filter);
    var guarantees = el('a', 'nav-guarantees', 'Guarantees');
    guarantees.href = 'guarantees.html';
    if (here === 'guarantees.html') guarantees.classList.add('current');
    side.appendChild(guarantees);
    side.appendChild(el('div', 'nav-label', 'Files'));
    side.appendChild(files);
    side.appendChild(outlineNav);

    filter.addEventListener('input', function () {
      var q = filter.value.trim().toLowerCase();
      side.querySelectorAll('nav a').forEach(function (a) {
        a.hidden = !!q && a.textContent.toLowerCase().indexOf(q) < 0;
      });
      files.querySelectorAll('details').forEach(function (d) {
        var any = !!d.querySelector('a:not([hidden])');
        d.hidden = !any;
        if (q && any) d.open = true;
      });
    });

    var scroller = document.querySelector('div.coq');
    // The outline item of the latest link, highlighted until the page scrolls away from where the link landed.
    var jumped = null;
    if (scroller && links.length) {
      var active = null;
      var spy = function () {
        var top = scroller.getBoundingClientRect().top + 80;
        var best = null;
        if (jumped && scroller.scrollTop === jumped.at) best = jumped.link;
        else links.forEach(function (l) {
          if (l.node.getBoundingClientRect().top <= top) best = l;
        });
        if (best === active) return;
        if (active) active.a.classList.remove('active');
        active = best;
        if (active) {
          active.a.classList.add('active');
          var a = active.a.getBoundingClientRect();
          var s = side.getBoundingClientRect();
          if (a.top < s.top + 8 || a.bottom > s.bottom - 8) {
            side.scrollTop += a.top - s.top - s.height / 2;
          }
        }
      };
      scroller.addEventListener('scroll', spy, { passive: true });
      spy();
    }

    // A link to a declaration lands on its comment. Comments render at load, which moves the target.
    var reveal = function () {
      if (!location.hash) return;
      var t = document.getElementById(decodeURIComponent(location.hash.slice(1)));
      if (!t) return;
      (commentOf(t) || t).scrollIntoView();
      var link = links.filter(function (l) { return l.node === t; })[0];
      if (link && spy) {
        jumped = { link: link, at: scroller.scrollTop };
        spy();
      }
    };
    reveal();
    window.addEventListener('load', reveal);
    window.addEventListener('hashchange', reveal);
  }

  // The Markdown comment directly above the declaration holding a name.
  function commentOf(name) {
    var n = name;
    while ((n = n.previousElementSibling)) {
      if (n.tagName !== 'SPAN') return null;
      if (n.classList.contains('vernacular')) {
        var above = n.previousElementSibling;
        return above && above.matches('div.doc.md') ? above : null;
      }
    }
    return null;
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();

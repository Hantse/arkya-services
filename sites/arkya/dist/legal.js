(() => {
  'use strict';
  const links = [...document.querySelectorAll('.legal-index li a')];
  const sections = links.map((link) => document.querySelector(link.hash)).filter(Boolean);
  if (!('IntersectionObserver' in window) || !sections.length) return;
  const visible = new Set();
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) visible.add(entry.target);
      else visible.delete(entry.target);
    }
    const current = sections.find((section) => visible.has(section));
    for (const link of links) {
      if (current && link.hash === `#${current.id}`) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    }
  }, { rootMargin: '-5% 0px -55% 0px', threshold: 0 });
  sections.forEach((section) => observer.observe(section));
})();

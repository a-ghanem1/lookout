import type { SidebarsConfig } from '@docusaurus/plugin-content-docs';

const sidebars: SidebarsConfig = {
  docs: [
    {
      type: 'category',
      label: 'Getting Started',
      collapsed: false,
      items: ['quickstart'],
    },
    {
      type: 'category',
      label: 'Reference',
      collapsed: false,
      items: ['configuration', 'extensibility'],
    },
    {
      type: 'category',
      label: 'Security & Operations',
      collapsed: false,
      items: ['security', 'troubleshooting', 'comparison'],
    },
  ],
};

export default sidebars;

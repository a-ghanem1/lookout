import { themes as prismThemes } from 'prism-react-renderer';
import type { Config } from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Lookout',
  tagline: 'Zero-config dev-time diagnostics for ASP.NET Core',
  favicon: 'img/favicon.png',

  url: 'https://a-ghanem1.github.io',
  baseUrl: '/Lookout/',

  organizationName: 'a-ghanem1',
  projectName: 'Lookout',
  trailingSlash: false,

  onBrokenLinks: 'throw',
  onBrokenMarkdownLinks: 'warn',

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        language: ['en'],
        indexBlog: false,
        searchResultLimits: 8,
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      },
    ],
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          sidebarPath: './sidebars.ts',
          editUrl: 'https://github.com/a-ghanem1/Lookout/edit/main/docs/',
          showLastUpdateTime: true,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'light',
      disableSwitch: false,
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Lookout',
      logo: {
        alt: 'Lookout logo',
        src: 'img/logo.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docs',
          position: 'left',
          label: 'Docs',
        },
        {
          to: '/docs/configuration',
          position: 'left',
          label: 'Config',
        },
        {
          href: 'https://github.com/a-ghanem1/Lookout',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://www.nuget.org/packages/Lookout.AspNetCore/',
          label: 'NuGet',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            { label: 'Quickstart', to: '/docs/quickstart' },
            { label: 'Configuration', to: '/docs/configuration' },
            { label: 'Security', to: '/docs/security' },
            { label: 'Troubleshooting', to: '/docs/troubleshooting' },
          ],
        },
        {
          title: 'Project',
          items: [
            {
              label: 'GitHub',
              href: 'https://github.com/a-ghanem1/Lookout',
            },
            {
              label: 'NuGet — Core',
              href: 'https://www.nuget.org/packages/Lookout.AspNetCore/',
            },
            {
              label: 'NuGet — Hangfire',
              href: 'https://www.nuget.org/packages/Lookout.Hangfire/',
            },
            {
              label: 'Issues',
              href: 'https://github.com/a-ghanem1/Lookout/issues',
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} Abdelrahman Ghanem. MIT License.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'json', 'powershell'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;

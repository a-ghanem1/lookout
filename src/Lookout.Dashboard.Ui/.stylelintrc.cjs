/**
 * Stylelint config enforcing design-system token usage.
 *
 * Component CSS may reference semantic tokens (--color-fg-default) and scale tokens
 * (--space-3, --radius-md) only. Raw hex, raw px, and primitive tokens (--gray-500)
 * are blocked outside the tokens/ directory.
 */
module.exports = {
  rules: {
    // Block raw hex in component CSS.
    'color-no-hex': [true, { message: 'Use semantic color tokens (--color-*), not raw hex.' }],

    // Block raw px values. Design scale is pre-tokenized.
    'declaration-property-unit-disallowed-list': [
      {
        '/.*/': ['px'],
      },
      {
        message: 'Use the spacing / radii scale tokens, not raw px.',
      },
    ],

    // Block primitive color refs (--gray-*, --accent-*, --success-*, etc.).
    'declaration-property-value-disallowed-list': [
      {
        '/.*/': [
          '/var\\(--gray-/',
          '/var\\(--accent-[0-9]/',
          '/var\\(--success-[0-9]/',
          '/var\\(--warn-[0-9]/',
          '/var\\(--error-[0-9]/',
          '/var\\(--info-[0-9]/',
          '/var\\(--special-[0-9]/',
        ],
      },
      {
        message: 'Reference semantic tokens (--color-*), not primitive steps.',
      },
    ],
  },
  overrides: [
    {
      // Token files ARE allowed to use raw hex, raw px, and primitive refs.
      files: ['tokens/**/*.css'],
      rules: {
        'color-no-hex': null,
        'declaration-property-unit-disallowed-list': null,
        'declaration-property-value-disallowed-list': null,
      },
    },
  ],
};

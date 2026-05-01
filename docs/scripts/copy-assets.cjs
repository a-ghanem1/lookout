#!/usr/bin/env node
'use strict';

const { copyFileSync, mkdirSync, existsSync } = require('fs');
const { join } = require('path');

const src = join(__dirname, '..', '..', 'assets', 'lookout-icon.png');
const destDir = join(__dirname, '..', 'static', 'img');

mkdirSync(destDir, { recursive: true });

if (existsSync(src)) {
  copyFileSync(src, join(destDir, 'logo.png'));
  copyFileSync(src, join(destDir, 'favicon.png'));
} else {
  // Non-fatal: CI copies explicitly; local devs need the assets/ directory.
  process.stderr.write(
    '[lookout-docs] assets/lookout-icon.png not found — skipping icon copy.\n'
  );
}

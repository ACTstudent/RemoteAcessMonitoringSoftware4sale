// Where the harnesses get their target and their credentials.
//
// Nothing here is hard-coded. These scripts drive a real server with real
// sign-ins, and a password committed to a repository is a password on the
// internet — even a synthetic one, because the next person copies the pattern.
//
// Set them in the shell before running:
//
//   CAMS_TEST_URL=https://localhost:5100 \
//   CAMS_TEST_ADMIN_USER=testadmin \
//   CAMS_TEST_ADMIN_PASSWORD='...' \
//   node ui-sweep.js routes.json fixture.json .
//
// Teacher and student credentials come from the fixture file that fixture.js
// writes, which is gitignored for the same reason.
'use strict';

function required(name, hint) {
  const value = process.env[name];
  if (!value) {
    console.error(`\n${name} is not set.\n  ${hint}\n`);
    process.exit(2);
  }
  return value;
}

const config = {
  /** The isolated server under test. Never point this at a live deployment. */
  baseUrl: process.env.CAMS_TEST_URL || 'https://localhost:5100',

  admin: {
    get user() { return required('CAMS_TEST_ADMIN_USER', 'The administrator to sign in as, e.g. testadmin'); },
    get password() { return required('CAMS_TEST_ADMIN_PASSWORD', 'That administrator\'s password.'); }
  },

  /** Reads the fixture written by fixture.js. */
  fixture(path) {
    const fs = require('fs');
    if (!fs.existsSync(path)) {
      console.error(`\nFixture not found: ${path}\n  Run fixture.js first; it creates the accounts these checks use.\n`);
      process.exit(2);
    }
    return JSON.parse(fs.readFileSync(path, 'utf8'));
  },

  /** The server presents its own generated LAN certificate. */
  acceptSelfSignedCertificate() {
    process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
  },

  /** Chrome, wherever it is on this machine. */
  chromePath() {
    const fs = require('fs');
    const found = [
      'C:/Program Files/Google/Chrome/Application/chrome.exe',
      'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
      process.env.LOCALAPPDATA + '/Google/Chrome/Application/chrome.exe'
    ].find(p => p && fs.existsSync(p));
    if (!found) {
      console.error('\nChrome was not found. Set CHROME_PATH or install Chrome.\n');
      process.exit(2);
    }
    return process.env.CHROME_PATH || found;
  }
};

module.exports = config;

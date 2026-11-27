#!/usr/bin/env node

/**
 * Builds the common-password list that Csag.Blueprint.Web embeds for password screening.
 *
 * The source is the xato.net list of the one million most common passwords, from the
 * ten million credentials Mark Burnett published in 2015, as SecLists redistributes it
 * under the MIT license. The download is pinned to one SecLists commit, so the output
 * only changes when SOURCE_COMMIT does.
 *
 * Passwords shorter than the policy minimum are already refused by the length rule, so a
 * list that is useful under every minimum needs enough long entries. The source is in
 * frequency order, and the script keeps the most common entries of each length band:
 * ENTRIES_PER_BAND of 8-9 characters, of 10-11, of 12-14 and of 15 or more. Under any
 * minimum length from 8 to 15 the list therefore holds at least ENTRIES_PER_BAND entries
 * that meet the policy. Entries are lowercased, because the check ignores case, and
 * entries that are not printable ASCII or are longer than the 100-character maximum of
 * the password rules are dropped.
 *
 * Usage:
 *   node scripts/build-common-passwords.mjs
 *
 * Writes packages/Csag.Blueprint.Web/Validation/Passwords/CommonPasswords.txt, one
 * password per line, sorted by ordinal comparison.
 */

import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const SOURCE_COMMIT = "c205c36a445bff37f8e58a9ec829105cd4975c58";
const SOURCE_URL = `https://raw.githubusercontent.com/danielmiessler/SecLists/${SOURCE_COMMIT}/Passwords/Common-Credentials/xato-net-10-million-passwords-1000000.txt`;
const ENTRIES_PER_BAND = 10_000;
const MAXIMUM_LENGTH = 100;
const BANDS = [
  { minimum: 8, maximum: 9 },
  { minimum: 10, maximum: 11 },
  { minimum: 12, maximum: 14 },
  { minimum: 15, maximum: MAXIMUM_LENGTH },
];

const repositoryRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const outputPath = join(
  repositoryRoot,
  "packages/Csag.Blueprint.Web/Validation/Passwords/CommonPasswords.txt",
);

const response = await fetch(SOURCE_URL);
if (!response.ok) {
  throw new Error(`Download failed: ${response.status} ${response.statusText} for ${SOURCE_URL}`);
}

const source = (await response.text()).split(/\r?\n/);
const printableAscii = /^[\x20-\x7e]+$/;
const kept = BANDS.map(() => new Set());

for (const line of source) {
  const password = line.toLowerCase();
  if (!printableAscii.test(password)) {
    continue;
  }

  const band = BANDS.findIndex(
    (candidate) => password.length >= candidate.minimum && password.length <= candidate.maximum,
  );
  if (band >= 0 && kept[band].size < ENTRIES_PER_BAND) {
    kept[band].add(password);
  }
}

for (const [index, entries] of kept.entries()) {
  const { minimum, maximum } = BANDS[index];
  if (entries.size < ENTRIES_PER_BAND) {
    throw new Error(
      `The ${minimum}-${maximum} character band has ${entries.size} entries, fewer than ${ENTRIES_PER_BAND}.`,
    );
  }
}

const passwords = kept.flatMap((entries) => [...entries]);
passwords.sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
writeFileSync(outputPath, `${passwords.join("\n")}\n`, "utf8");

console.log(`Wrote ${passwords.length} passwords to ${outputPath}`);

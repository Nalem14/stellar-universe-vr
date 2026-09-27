#!/usr/bin/env python3
"""
i18n.py — intégration des clés de docs/i18n/missing-keys.md dans les dix langues.

Le fichier missing-keys.md est tenu par l'agent VR : il liste les clés dont le
client a besoin, avec leurs textes EN et FR. Ce script fait le reste, et ne
dépend d'aucune mise en forme du document (séparateurs `---`, colonnes
supplémentaires, ordre des sections) — c'est ce qui le rend utilisable à chaque
lot sans le réécrire.

    python3 docs/tools/i18n.py check
        État des dix langues : jeu de clés, ordre, placeholders, vides, part
        traduite. Sort en erreur si une langue diverge.

    python3 docs/tools/i18n.py pending
        Clés de la section « À intégrer » absentes d'au moins une langue.

    python3 docs/tools/i18n.py add translations.json [--dry-run]
        Insère des traductions dans les dix fichiers. Le JSON est un objet
        {clé: {langue: texte}} ; `en` et `fr` sont obligatoires, les autres
        langues sont celles que tu as rédigées — une langue absente garde la
        valeur anglaise, donc rien ne casse, mais `pending` le signale.
        Chaque clé est posée juste après la dernière clé de sa famille
        (`crew.tactical.` pour `crew.tactical.alertRed.1`), pour que les
        familles restent groupées.

    python3 docs/tools/i18n.py cleanup
        Retire de missing-keys.md les sections « À intégrer » dont toutes les
        clés sont intégrées, et ajoute la ligne de traçabilité dans « Intégré
        côté web ».

Les huit traductions non EN/FR ne sont pas devinées : elles s'écrivent, en
reprenant la terminologie déjà présente dans chaque fichier (le rendu de
« Commander », de « alerte rouge », de « canal »… — voir `check --families`).
"""
import json
import os
import re
import sys

WEB = '/Users/thommy/Websites/stellar-universe'
VR = '/Users/thommy/Unity/Games/stellar-universe-vr'
LANGS_DIR = f'{WEB}/assets/langs'
MISSING_KEYS = f'{VR}/docs/i18n/missing-keys.md'

# L'anglais est la référence (jeu de clés, ordre, placeholders) ; le français
# est la langue d'origine du jeu. Les autres suivent.
REFERENCE = 'en'
LANGS = ['en', 'fr', 'de', 'es', 'it', 'pt', 'ru', 'ko', 'ja', 'zh']

ROW = re.compile(r'^\|\s*`([^`]+)`\s*\|([^|]+)\|([^|]+)\|', re.M)
HEADING = re.compile(r'^##\s+(.*)$', re.M)


def table(code):
    with open(f'{LANGS_DIR}/{code}.json', encoding='utf-8') as f:
        return json.load(f)


def write_table(code, data):
    with open(f'{LANGS_DIR}/{code}.json', 'w', encoding='utf-8') as f:
        f.write(json.dumps(data, ensure_ascii=False, indent=2) + '\n')


def placeholders(text):
    return sorted(re.findall(r'\{\d+\}', str(text)))


def sections(text, wanted):
    """Découpe le document en sections `## …`, sans dépendre des `---`."""
    marks = [(m.start(), m.group(1).strip()) for m in HEADING.finditer(text)]
    out = []
    for i, (start, title) in enumerate(marks):
        end = marks[i + 1][0] if i + 1 < len(marks) else len(text)
        if wanted(title):
            out.append((start, end, title, text[start:end]))
    return out


def integrate_sections(text):
    return sections(text, lambda t: t.lower().startswith('à intégrer'))


def pending_keys(text):
    """{clé: (EN, FR)} des sections « À intégrer », quelle que soit la mise en
    forme (le tableau peut avoir une colonne de contexte en plus)."""
    out = {}
    for _, _, _, body in integrate_sections(text):
        for key, en, fr in ROW.findall(body):
            out[key] = (en.strip(), fr.strip())
    return out


def family(key):
    """Préfixe de famille d'une clé, pour regrouper les nouvelles clés avec
    leurs voisines.

    `crew.tactical.alertRed.1` → `crew.tactical.` : l'index de variante d'une
    réplique (`1`, `2`…) ne fait pas partie de la famille, sinon une famille
    neuve (aucune clé existante en `crew.tactical.alertRed.`) ne trouverait
    aucune ancre et la clé partirait à la fin du fichier.
    `vr.screen.auto` → `vr.screen.` ; `orphan` → '' (pas de famille).
    """
    parts = key.split('.')
    if len(parts) < 2:
        return ''
    if parts[-1].isdigit():
        parts = parts[:-1]
    return '.'.join(parts[:-1]) + '.' if len(parts) > 1 else ''


def anchor_for(keys, key):
    """Dernière clé existante de la même famille — `None` si la famille est neuve."""
    fam = family(key)
    if not fam:
        return None
    same = [k for k in keys if k.startswith(fam)]
    return same[-1] if same else None


def insert(data, key, value):
    """Pose la clé après la dernière de sa famille, sinon après la dernière clé
    du même préfixe racine (`vr.`, `crew.`), sinon à la fin."""
    keys = list(data)
    anchor = anchor_for(keys, key) or anchor_for(keys, key.split('.')[0] + '.')
    if anchor is None:
        keys.append(key)
    else:
        keys.insert(keys.index(anchor) + 1, key)
    return {k: (value if k == key else data[k]) for k in keys}


def cmd_check():
    ref = table(REFERENCE)
    ref_keys = list(ref)
    failures = 0
    print(f"{'langue':7} {'clés':>5} {'jeu':>5} {'ordre':>6} {'vides':>6} {'placeholders':>13} {'= anglais':>10} {'traduit':>8}")
    for code in LANGS:
        data = table(code)
        keys = list(data)
        same_set = set(keys) == set(ref_keys)
        # L'ordre en/fr diverge depuis toujours sur un bloc de descriptions : on
        # compare les langues entre elles, pas au français.
        same_order = keys == ref_keys or code == 'fr'
        empty = [k for k in ref_keys if k in data and not str(data[k]).strip()]
        bad_ph = [k for k in ref_keys if k in data and placeholders(data[k]) != placeholders(ref[k])]
        same_en = sum(1 for k in ref_keys if k in data and data[k] == ref[k])
        translated = (100.0 * (len(ref_keys) - same_en) / max(1, len(ref_keys))
                      if code != REFERENCE else None)
        bad = not (same_set and same_order) or bool(empty) or bool(bad_ph)
        failures += 1 if bad else 0
        pct = '      —' if translated is None else f'{translated:6.0f}%'
        print(f"{code:7} {len(keys):5} {str(same_set):>5} {str(same_order):>6} {len(empty):6} "
              f"{len(bad_ph):13} {same_en:10} {pct}"
              + ('   <-- à corriger' if bad else ''))
    return 1 if failures else 0


def cmd_pending():
    text = open(MISSING_KEYS, encoding='utf-8').read()
    listed = pending_keys(text)
    if not listed:
        print('Aucune section « À intégrer » : rien en attente.')
        return 0

    missing_anywhere = 0
    for key, (en, fr) in listed.items():
        absent = [c for c in LANGS if key not in table(c)]
        if absent:
            missing_anywhere += 1
            print(f'{key:34} absente de : {", ".join(absent)}')
        else:
            print(f'{key:34} intégrée dans les {len(LANGS)} langues')
    print(f'\n{len(listed)} clé(s) listée(s), {missing_anywhere} à intégrer.')
    return 1 if missing_anywhere else 0


def cmd_add(path, dry_run=False):
    with open(path, encoding='utf-8') as f:
        new = json.load(f)
    for key, values in new.items():
        for required in ('en', 'fr'):
            if required not in values:
                print(f'{key}: valeur « {required} » manquante')
                return 1

    for code in LANGS:
        data = table(code)
        for key, values in new.items():
            if key in data:
                print(f'{code}: {key} existe déjà, ignorée')
                continue
            data = insert(data, key, values.get(code, values['en']))
        if dry_run:
            print(f'{code}: {len(new)} clé(s) à insérer (essai à blanc)')
        else:
            write_table(code, data)
            print(f'{code}: {len(new)} clé(s) insérée(s), {len(data)} au total')

    if dry_run:
        return 0

    for key, values in new.items():
        for code in LANGS:
            got = table(code)[key]
            want = values.get(code, values['en'])
            if got != want:
                print(f'ERREUR {code}/{key} : {got!r} au lieu de {want!r}')
                return 1
    print('Vérification : valeurs conformes dans les dix langues.')
    return cmd_check()


def cmd_cleanup():
    text = open(MISSING_KEYS, encoding='utf-8').read()
    listed = pending_keys(text)
    if not listed:
        print('Aucune section « À intégrer » à retirer.')
        return 0

    done = [k for k in listed if all(k in table(c) for c in LANGS)]
    left = [k for k in listed if k not in done]
    if not done:
        print(f'Rien à retirer : {len(left)} clé(s) encore absente(s) d\'au moins une langue.')
        return 1
    if left:
        print('Sections conservées (clés encore manquantes) : ' + ', '.join(left))
        return 1

    families = sorted({family(k).rstrip('.') for k in done})
    bullet = (f"- {', '.join('`%s`' % k for k in sorted(done))} "
              f"dans **les dix langues** (familles : {', '.join(families)}).\n")

    # Trace en fin de « Intégré côté web », puis retrait des sections intégrées.
    target = sections(text, lambda t: t.startswith('Intégré côté web'))
    if not target:
        print('Section « Intégré côté web » introuvable dans missing-keys.md.')
        return 1
    start, end, _, body = target[0]
    # La ligne va dans la section, donc AVANT le `---` qui la sépare de la
    # suivante — et il faut une ligne vide après elle.
    body = body.rstrip('\n')
    tail = ''
    if body.endswith('---'):
        body = body[:-3].rstrip('\n')
        tail = '\n---'
    text = text[:start] + body + '\n' + bullet + tail + '\n\n' + text[end:]

    for start, end, _, body in reversed(integrate_sections(text)):
        # Le `---` qui séparait la section retirée n'a plus lieu d'être.
        head = text[:start]
        head = re.sub(r'\n---\n\n$', '\n', head)
        text = head + text[end:]

    with open(MISSING_KEYS, 'w', encoding='utf-8') as f:
        f.write(text)
    print(f'{len(done)} clé(s) retirée(s) du document ; ligne de traçabilité ajoutée.')
    return 0


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    cmd = argv[1]
    if cmd == 'check':
        return cmd_check()
    if cmd == 'pending':
        return cmd_pending()
    if cmd == 'add':
        if len(argv) < 3:
            print('usage : i18n.py add translations.json [--dry-run]')
            return 1
        return cmd_add(argv[2], '--dry-run' in argv)
    if cmd == 'cleanup':
        return cmd_cleanup()
    print(__doc__)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))

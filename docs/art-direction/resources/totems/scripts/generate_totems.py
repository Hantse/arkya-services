"""Compatibility entry point for the revised licensed anatomy/IK pipeline.

The rejected V1 primitive generator is retired. Regenerate anatomy explicitly
with build_anatomy.py; this command rebuilds coats, animations and exports.
"""
import argparse
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_totems_v2 import build

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--species', choices=['wolf', 'fox'])
    parser.add_argument('--draft', action='store_true')
    parser.add_argument('--lookdev', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for species in ([args.species] if args.species else ['wolf', 'fox']):
        build(species, args.draft, args.lookdev)

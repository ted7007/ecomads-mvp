#!/usr/bin/env python3
"""Exercise the actual release-retention block with protected releases and an escaping symlink."""
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile

source=Path(sys.argv[1] if len(sys.argv)>1 else '/opt/ecomads/deploy-vps.sh').read_text()
start=source.index('mapfile -t expired_releases')
end=source.index('mapfile -t expired_backups',start)
with tempfile.TemporaryDirectory(prefix='ecomads-retention-') as temporary:
    fixture=Path(temporary)
    root=fixture/'app'
    releases=root/'releases'
    releases.mkdir(parents=True)
    names=['zz-6','zz-5','zz-4','zz-3','zz-2','zz-1','b-current','a-previous','0-expired']
    for name in names:
        (releases/name).mkdir()
        (releases/name/'source.txt').write_text(name)
    outside=fixture/'outside'
    outside.mkdir()
    (outside/'keep.txt').write_text('preserve')
    (releases/'00-outside').symlink_to(outside,target_is_directory=True)
    prelude='set -Eeuo pipefail\nROOT='+shlex.quote(str(root))+'\nnew_image=ecomads:b-current\nprevious_image=ecomads:a-previous\ndocker() { return 0; }\n'
    subprocess.run(['bash','-c',prelude+source[start:end]],check=True)
    assert (releases/'b-current'/'source.txt').exists(),'Current source deleted'
    assert (releases/'a-previous'/'source.txt').exists(),'Previous source deleted'
    assert not (releases/'0-expired').exists(),'Expired source retained'
    assert not (releases/'zz-1').exists(),'Expired release retained'
    assert (outside/'keep.txt').read_text()=='preserve','Escaped release path modified'
    assert (releases/'00-outside').is_symlink(),'Escaping symlink deleted'
print('Retention checks passed: current, previous, expired, escaping symlink')

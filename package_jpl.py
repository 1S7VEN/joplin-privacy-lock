import pathlib,tarfile,json
root=pathlib.Path(__file__).resolve().parent
dist=root/'dist';out=root/'releases';out.mkdir(exist_ok=True)
with tarfile.open(out/'local.joplin.minimize-lock.jpl','w',format=tarfile.USTAR_FORMAT) as tar:
 for name in ['manifest.json','index.js','LockHost.exe']:tar.add(dist/name,arcname=name)
print('Built releases/local.joplin.minimize-lock.jpl')

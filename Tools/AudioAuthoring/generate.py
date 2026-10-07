#!/usr/bin/env python3
"""Original Sanctuary score + layered foley/synthesis. No samples, pretrained music, or borrowed melodies.
Offline only: NumPy/SciPy versions in manifest. Runtime only loads pre-rendered clips.
"""
from pathlib import Path
import argparse, hashlib, json, math, platform, wave
import numpy as np
import scipy
from scipy.signal import butter, sosfilt, resample_poly

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Assets/SinglePlayerFoundation/Resources/Audio/Sanctuary'
SR = 44100
SEED = 7012026
SFX_DURATIONS = {'knife_swing':.25,'sword_swing':.39,'bow_draw':.62,'bow_release':.40,'staff_cast':.78,'impact_light':.25,'impact_heavy':.48,'hurt':.48,'heal':1.15,'equip':.43,'ui_confirm':.32,'ui_cancel':.3}
SCORE = {
 'exploration': {'title': 'Ivory Steps in the Bronze Garden', 'bpm': 80, 'bars': 16, 'mode': 'D-centered minor/modal; sixth omitted',
   'chords': [[50,57,60,64],[53,60,64,69],[48,55,62,64],[55,62,65,69]] * 4,
   # A 16-bar call/answer, deliberately sparse, including rests and displaced endings.
   'melody': [[(0,74,1.5),(2,77,1),(3.5,76,.5)],[(1,72,1),(2.5,69,1)],[(0,74,2),(3,72,.8)],[(1,69,1),(2.5,67,1)],
              [(0,69,1),(1.5,72,.5),(2.5,74,1)],[(.5,76,1.5),(3,72,.8)],[(0,74,1),(2,81,1.5)],[(1,79,1),(2.5,76,1)],
              [(0,77,1.5),(2,74,1),(3.5,72,.5)],[(1,69,1),(2.5,72,1)],[(0,76,2),(3,74,.8)],[(1,72,1),(2.5,67,1)],
              [(0,69,1),(1.5,74,.5),(2.5,77,1)],[(.5,76,1.5),(3,72,.8)],[(0,74,2),(2.5,69,.7)],[(1,67,1),(3,69,.8)]]},
 'combat': {'title': 'The Amber Gate Holds', 'bpm': 120, 'bars': 16, 'mode': 'D minor / suspended cadence',
   'chords': [[38,45,50,53],[41,48,53,57],[36,43,48,52],[43,50,53,57]] * 4,
   'melody': [[(0,74,.7),(1,74,.4),(1.75,77,.5),(2.5,76,.7),(3.5,72,.4)],[(.5,69,.6),(1.5,72,.6),(2.5,77,.8)],
              [(0,76,.8),(1,72,.6),(2,74,.8),(3.25,69,.5)],[(0,67,1),(1.5,69,.5),(2.5,72,.9)],
              [(0,74,.6),(1,77,.6),(2,81,.8),(3.25,79,.5)],[(0,77,.8),(1.5,76,.6),(2.5,72,.8)],
              [(0,74,1),(1.5,72,.5),(2.5,69,.7)],[(.5,67,.7),(1.75,69,.5),(3,72,.7)],
              [(0,77,.7),(1,74,.4),(1.75,72,.5),(2.5,76,.7),(3.5,77,.4)],[(.5,81,.6),(1.5,79,.6),(2.5,77,.8)],
              [(0,76,.8),(1,72,.6),(2,74,.8),(3.25,69,.5)],[(0,67,1),(1.5,72,.5),(2.5,69,.9)],
              [(0,74,.6),(1,77,.6),(2,81,.8),(3.25,84,.5)],[(0,81,.8),(1.5,79,.6),(2.5,76,.8)],
              [(0,77,1),(1.5,74,.5),(2.5,72,.7)],[(.5,69,.7),(1.75,67,.5),(3,69,.7)]]}}

def hz(note): return 440 * 2 ** ((note-69)/12)
def env(t, duration, attack=.006, release=.08):
    return np.minimum(1, t/max(.0001,attack)) * np.minimum(1, np.maximum(0,duration-t)/max(.0001,release))
def filtered(x, sr, cutoff, high=False):
    return sosfilt(butter(2, cutoff, 'highpass' if high else 'lowpass', fs=sr, output='sos'),x)
def noise(rng, n, sr, low=8000, high=100):
    return filtered(filtered(rng.standard_normal(n),sr,low),sr,high,True)
def tone(note,duration,instrument,rng,sr=SR):
    t=np.arange(round(duration*sr))/sr; f=hz(note); x=np.zeros(len(t))
    if instrument=='lyre':
        for h in range(1,13):
            x += np.sin(2*np.pi*f*h*t + .12*h) * np.exp(-t*(2.5+h*.65))/h**1.25
        x += noise(rng,len(t),sr,6500,900)*np.exp(-t*55)*.06
        x *= env(t,duration,.002,.12)
    elif instrument=='bronze':
        for ratio,gain,decay in [(1,1,1.5),(2.01,.43,2.2),(2.76,.22,3),(4.12,.09,5.2),(5.43,.04,7)]:
            x += np.sin(2*np.pi*f*ratio*t)*gain*np.exp(-t*decay)
        x *= env(t,duration,.003,.2)*.5
    elif instrument=='flute':
        phase=2*np.pi*f*t+.016*np.sin(2*np.pi*4.6*t)
        x=(np.sin(phase)+.15*np.sin(2*phase)+.055*np.sin(3*phase))
        x += noise(rng,len(t),sr,3000,500)*.045
        x *= env(t,duration,.065,.14)*(.88+.12*np.sin(np.pi*t/duration))
    elif instrument=='strings':
        for h in range(1,8):
            x += (np.sin(2*np.pi*f*h*t)+.55*np.sin(2*np.pi*f*1.0017*h*t+.7))/h**1.5
        x *= env(t,duration,.2,.4)*.3
    elif instrument=='bass':
        x=(np.sin(2*np.pi*f*t)+.2*np.sin(4*np.pi*f*t)+.07*np.sin(6*np.pi*f*t))*np.exp(-t*.8)*env(t,duration,.009,.1)
    elif instrument=='horn':
        for h in range(1,9): x+=np.sin(2*np.pi*f*h*t+.003*h*np.sin(2*np.pi*5*t))*np.exp(-h/3)/h**.5
        x *= env(t,duration,.045,.14)*.75
    return x

def drum(kind,rng,sr=SR,scale=1):
    dur={'frame':.5,'low':.7,'rim':.16,'shaker':.11}[kind]*scale
    t=np.arange(round(sr*dur))/sr
    if kind in ('frame','low'):
        f=90 if kind=='frame' else 55
        phase=2*np.pi*(f*t + f*.45*.025*(1-np.exp(-t/.025)))
        x=np.sin(phase)*np.exp(-t*8/scale)+.28*np.sin(phase*1.59)*np.exp(-t*16/scale)
        x+=noise(rng,len(t),sr,4000,250)*np.exp(-t*60/scale)*.4
    elif kind=='rim':
        x=(np.sin(2*np.pi*430*t)+.4*np.sin(2*np.pi*710*t))*np.exp(-t*45)+noise(rng,len(t),sr,6500,1300)*np.exp(-t*65)*.5
    else: x=noise(rng,len(t),sr,10000,3500)*np.exp(-t*48)*.45
    return x*env(t,dur,.001,.012)

def put(mix,x,seconds,gain,pan):
    index=(np.arange(len(x))+round(seconds*SR))%len(mix)
    left=math.cos((pan+1)*math.pi/4);right=math.sin((pan+1)*math.pi/4)
    np.add.at(mix[:,0],index,x*gain*left);np.add.at(mix[:,1],index,x*gain*right)

def music(key):
    spec=SCORE[key]; beat=60/spec['bpm']; length=round(spec['bars']*4*beat*SR)
    mix=np.zeros((length,2));rng=np.random.default_rng(SEED+(key=='combat'));events=[]
    def add(inst,note,at,dur,gain,pan):
        x=drum(inst,rng) if inst in ('frame','low','rim','shaker') else tone(note,dur,inst,rng)
        # The score's rhythmic gate and percussion sample length can differ.
        # Record the actual rendered sample length rather than the unused drum gate.
        events.append([inst,note,round(at,4),len(x)/SR,gain,pan])
        put(mix,x,at,gain,pan)
    for bar,chord in enumerate(spec['chords']):
        at=bar*4*beat
        for j,n in enumerate(chord): add('strings',n+(12 if key=='combat' else 0),at,4.8*beat,.095 if key=='exploration' else .10,[-.55,.4,-.2,.65][j])
        if key=='exploration':
            for i,j in enumerate([0,2,1,3,2,1]): add('lyre',chord[j]+12,at+(i*.5+.25)*beat,1.3,.13 if i%2==0 else .10,-.35)
            add('bass',chord[0]-12,at,3.8*beat,.16,0)
            if bar%2==0: add('bronze',chord[2]+12,at+3*beat,2.5,.08,.6)
            add('frame',0,at,.5,.09,-.15)
            add('shaker',0,at+2.5*beat,.1,.035,.5)
        else:
            for i in range(8):
                n=chord[[0,2,0,3,1,2,0,1][i]]+12
                add('lyre',n,at+i*.5*beat,.8,.13 if i in (0,3,6) else .095,-.4)
                add('shaker',0,at+(i*.5+.25)*beat,.1,.09 if i%2==0 else .05,.55)
            for offset in [0,1.5,2,3.5]: add('bass',chord[0],at+offset*beat,.65,.19,0)
            for offset in [0,2,3.5]: add('low',0,at+offset*beat,.7,.22,0)
            for offset in [1,3]: add('frame',0,at+offset*beat,.5,.2,-.1)
            for offset in ([2.75,3.25,3.75] if bar%4==3 else [1.75]): add('rim',0,at+offset*beat,.16,.11,.25)
            if bar%4==0: add('bronze',chord[2]+24,at,2.4,.10,.5)
        for offset,n,dur in spec['melody'][bar]:
            add('flute' if key=='exploration' else 'horn',n,at+offset*beat,dur*beat,.17 if key=='exploration' else .20,.1)
    # Circular multi-tap room and dotted stereo reflections retain tails across the loop boundary.
    dry=mix.copy()
    for seconds,gain,swap in [(.071,.09,False),(.113,.07,True),(.173,.055,False),(beat*.75,.12,True),(beat*1.5,.055,False)]:
        mix+=np.roll(dry[:,::-1] if swap else dry,round(seconds*SR),axis=0)*gain
    # Periodic zero-phase bandwidth/DC conditioning preserves exact loop timing.
    f=np.fft.rfftfreq(length,1/SR); shape=(1-np.exp(-(f/32)**4))*np.exp(-(f/14500)**8)
    mix=np.fft.irfft(np.fft.rfft(mix,axis=0)*shape[:,None],n=length,axis=0)
    mix=np.tanh(mix*1.1);mix-=mix.mean(axis=0);mix*=.78/np.max(np.abs(mix))
    return mix,events

def sfx(name,index):
    sr=24000;rng=np.random.default_rng(SEED+100+index)
    dur=SFX_DURATIONS[name]
    t=np.arange(round(sr*dur))/sr;x=np.zeros(len(t));n=noise(rng,len(t),sr,9000,180)
    def partial(f,decay,gain=1,delay=0):
        q=np.maximum(0,t-delay);return (t>=delay)*np.sin(2*np.pi*f*q)*np.exp(-q*decay)*gain
    if 'swing' in name:
        heavy=name=='sword_swing';center=.14 if heavy else .085
        sweep=np.exp(-((t-center)/(.07 if heavy else .043))**2)
        x=filtered(n,sr,2200 if heavy else 4700)*sweep*.9
        for f,g,d in [(730,.13,15),(1417,.08,20),(2113,.035,28)]: x+=partial(f*(.8 if heavy else 1),d,g,.055 if heavy else .025)
        x+=partial(125 if heavy else 210,24,.18)*sweep
    elif name=='bow_draw':
        x=filtered(n,sr,2200)*(.15+.85*t/dur)*.23*(.75+.25*np.sin(2*np.pi*19*t))
        phase=2*np.pi*(120*t+90*t*t)
        x+=(np.sin(phase)+.28*np.sin(phase*2.7))*np.sin(np.pi*t/dur)**.6*.10
        for delay in [.11,.28,.43]:x+=partial(630,60,.1,delay)
    elif name=='bow_release':
        for f,g,d in [(173,1,12),(346,.42,20),(692,.18,28),(1127,.08,34)]:x+=partial(f,d,g)*.48
        x+=n*np.exp(-t*50)*.52+filtered(n,sr,3500)*np.exp(-((t-.095)/.055)**2)*.25
    elif name=='staff_cast':
        x=n*np.exp(-((t-.12)/.07)**2)*.22
        for i,(f,g) in enumerate([(293.66,.35),(440,.23),(587.33,.18),(880,.10),(1244.5,.08)]):
            x+=partial(f,4+i,g,.035*i)*env(t,dur,.014,.22)
        phase=2*np.pi*(190*t+450*t*t);x+=np.sin(phase)*np.exp(-((t-.14)/.13)**2)*.25
    elif name.startswith('impact'):
        heavy=name=='impact_heavy';phase=2*np.pi*((60 if heavy else 125)*t+2*(1-np.exp(-t*40)))
        x=np.sin(phase)*np.exp(-t*(9 if heavy else 20))*.7
        x+=n*np.exp(-t*(40 if heavy else 70))*.7
        for f,g,d in [(347,.20,25),(891,.12,35),(1531,.055,40)]:x+=partial(f*(.7 if heavy else 1),d,g)
        x+=filtered(n,sr,1200)*np.exp(-t*13)*(.26 if heavy else .1)
    elif name=='hurt':
        phase=2*np.pi*(95*t+5*(1-np.exp(-t*10)))
        x=(np.sin(phase)+.21*np.sin(phase*2))*np.exp(-t*8)*.6
        x+=filtered(n,sr,1600)*np.exp(-t*12)*.4+partial(460,22,.13)
    elif name in ('heal','ui_confirm','ui_cancel'):
        notes=[62,69,74,77] if name=='heal' else [74,81] if name=='ui_confirm' else [72,65]
        for j,m in enumerate(notes):
            delay=j*(.13 if name=='heal' else .065);f=hz(m)
            x+=partial(f,5 if name=='heal' else 16,.27,delay)+partial(f*2.76,11,.045,delay)
        x+=filtered(n,sr,5000)*np.exp(-t*30)*.045
        if name=='heal': x+=np.sin(2*np.pi*hz(50)*t)*env(t,dur,.16,.6)*.12
    elif name=='equip':
        for delay,gain in [(0,.8),(.10,1),(.18,.45)]:
            q=np.maximum(0,t-delay);x+=(t>=delay)*n*np.exp(-q*75)*gain*.4
            for f,g in [(621,.21),(1073,.14),(1931,.07)]:x+=partial(f,23,g*gain,delay)
        x+=partial(170,20,.18,.10)
    x=filtered(x,sr,55,True)
    # Short dry Foley with a bounded stone reflection, never extends declared duration.
    delay=round(.043*sr);x[delay:]+=x[:-delay].copy()*.1
    x*=env(t,dur,.002,.045)
    # Weighted DC removal keeps the attack/release at zero without leaving the
    # residual DC produced by subtracting the mean times an unnormalised window.
    dc_window=env(t,dur,.015,.045);dc_window[-1]=0;x[-1]=0
    x-=x.mean()*dc_window/dc_window.mean()
    x*=.72/max(.001,np.max(np.abs(x)));x[:1]=0;x[-1:]=0
    return x,sr

def save(path,x,sr):
    x=np.asarray(x)
    if x.ndim not in (1,2) or not len(x) or (x.ndim==2 and x.shape[1] not in (1,2)):
        raise ValueError('Expected nonempty mono or stereo audio.')
    if not np.isfinite(x).all() or np.max(np.abs(x))>1:
        raise ValueError('Refusing non-finite or out-of-range audio; do not conceal clipping.')
    if not isinstance(sr,int) or sr<=0:
        raise ValueError('Sample rate must be a positive integer.')
    x=np.round(x*32767).astype('<i2')
    with wave.open(str(path),'wb') as w:
        w.setnchannels(1 if x.ndim==1 else x.shape[1]);w.setsampwidth(2);w.setframerate(sr);w.writeframes(x.tobytes())
    return x.astype(float)/32768

def meta(path,music=False):
    guid=hashlib.md5(('SPGameFoundation/OriginalSanctuaryAudio/'+path.name).encode()).hexdigest()
    path.with_suffix(path.suffix+'.meta').write_text(f'''fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: {2 if music else 0}
    sampleRateSetting: {0 if music else 2}
    sampleRateOverride: {44100 if music else 24000}
    compressionFormat: {1 if music else 0}
    quality: {0.7 if music else 1}
    conversionMode: 0
    preloadAudioData: {0 if music else 1}
  platformSettingOverrides: {{}}
  forceToMono: {0 if music else 1}
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData: Original deterministic Sanctuary audio; see Tools/AudioAuthoring
  assetBundleName:
  assetBundleVariant:
''')

def stats(path,x,sr,loop):
    peak=float(np.max(np.abs(x)));rms=float(np.sqrt(np.mean(x*x)))
    # Periodic padding for loops; silence padding for one-shots. This is a
    # reproducible 4x FIR interpolation estimate, not a certified dBTP meter.
    pad=64
    padded=np.concatenate((x[-pad:],x,x[:pad])) if loop else np.pad(x,((pad,pad),(0,0)) if x.ndim==2 else (pad,pad))
    oversampled=resample_poly(padded,4,1,axis=0)[pad*4:-pad*4]
    channels=1 if x.ndim==1 else x.shape[1]
    step=x[0]-x[-1]
    slope_error=max(float(np.max(np.abs(step-(x[-1]-x[-2])))),float(np.max(np.abs((x[1]-x[0])-step))))
    try: filename=path.relative_to(ROOT).as_posix()
    except ValueError: filename=path.name
    record={'file':filename,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bytes':path.stat().st_size,'sample_rate':sr,'channels':channels,'sample_width_bytes':2,'samples':len(x),'duration_seconds':len(x)/sr,'peak_dbfs':20*math.log10(max(peak,1e-12)),'rms_dbfs':20*math.log10(max(rms,1e-12)),'oversampled_peak_dbfs':20*math.log10(max(float(np.max(np.abs(oversampled))),1e-12)),'dc':np.mean(x,axis=0).tolist(),'clipped_samples':int(np.sum(np.abs(x)>=.999)),'loop':loop,'endpoint_step':float(np.max(np.abs(step))),'seam_slope_error':slope_error,'max_adjacent_step':float(np.max(np.abs(np.diff(x,axis=0)))),'float32_decoded_payload_bytes':int(x.size*4)}
    if channels==2:
        record['mono_rms_ratio']=float(np.sqrt(np.mean(x.mean(axis=1)**2))/rms)
        record['stereo_correlation']=float(np.corrcoef(x.T)[0,1])
    return record

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--out',type=Path,default=DEST)
    parser.add_argument('--metadata-out',type=Path);parser.add_argument('--audition-out',type=Path)
    args=parser.parse_args();args.out=args.out.resolve();args.out.mkdir(parents=True,exist_ok=True)
    records=[];authored={};reel=[];audition_cues=[];cursor=0
    for key in SCORE:
        x,events=music(key);path=args.out/(key+'.wav');y=save(path,x,SR);meta(path,True);records.append(stats(path,y,SR,True));authored[key]=events
    for i,name in enumerate(SFX_DURATIONS):
        x,sr=sfx(name,i);path=args.out/(name+'.wav');y=save(path,x,sr);meta(path);records.append(stats(path,y,sr,False));reel.extend([x,np.zeros(sr//2),x,np.zeros(sr)])
        audition_cues.append({'name':name,'first_seconds':cursor/sr,'second_seconds':(cursor+len(y)+sr//2)/sr})
        cursor+=2*len(y)+3*sr//2
    source=Path(__file__).resolve().parent
    # Alternate output is isolated by default, including metadata and preview.
    here=args.metadata_out or (source if args.out==DEST else args.out/'authoring')
    here.mkdir(parents=True,exist_ok=True)
    preview=args.audition_out or (ROOT/'Artifacts/audio' if args.out==DEST else args.out/'audition')
    preview.mkdir(parents=True,exist_ok=True)
    audition_path=preview/'Sanctuary_SFX_Audition.wav';audition=save(audition_path,np.concatenate(reel),24000)
    manifest={'schema_version':2,'generator':'generate.py','generator_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'seed':SEED,'python':platform.python_version(),'numpy':np.__version__,'scipy':scipy.__version__,'event_fields':['instrument','midi_note_or_zero_for_percussion','start_seconds','rendered_duration_seconds','gain','pan'],'listening_qa':'Not performed: no audio audition tool available. Objective checks do not prove perceived speaker quality.','native_import_qa':'Offline metadata verification only; Unity and physical Android/iOS import, streaming and listening require separate acceptance.','oversampled_peak_method':'4x scipy.signal.resample_poly default FIR; 64-sample periodic/silent padding; not a certified true-peak meter.','license':'CC0-1.0 original project-authored compositions, synthesized instruments and Foley; no external recordings or melodies.','license_url':'https://creativecommons.org/publicdomain/zero/1.0/','assets':records,'audition':stats(audition_path,audition,24000,False),'totals':{'source_wav_bytes':sum(r['bytes'] for r in records),'sfx_float32_decoded_payload_bytes':sum(r['float32_decoded_payload_bytes'] for r in records if not r['loop'])}}
    (here/'score.json').write_text(json.dumps(SCORE,indent=2)+'\n');(here/'events.json').write_text(json.dumps(authored,indent=2)+'\n')
    manifest['score_sha256']=hashlib.sha256((here/'score.json').read_bytes()).hexdigest()
    manifest['events_sha256']=hashlib.sha256((here/'events.json').read_bytes()).hexdigest()
    manifest['audition_cues']=audition_cues
    (here/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(manifest,indent=2))
if __name__=='__main__':main()

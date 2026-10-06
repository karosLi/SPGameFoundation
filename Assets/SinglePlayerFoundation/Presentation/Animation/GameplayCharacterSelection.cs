using SPF.Contracts;
using Unity.Mathematics;

namespace SPF.Presentation.Animation
{
    /// <summary>Bounded nearest-first max heap. Equal-distance actors use stable handles, never dense rows.
    /// A caller can retain cheap sprites for every unselected row. No allocations after construction.</summary>
    public sealed class GameplayCharacterSelection
    {
        struct Candidate { public int Row; public EntityHandle Handle; public float Distance; }
        readonly Candidate[] m_Heap;
        int m_Budget;
        float2 m_Focus;
        public int Count { get; private set; }
        public GameplayCharacterSelection(int capacity) { m_Heap=new Candidate[math.max(1,capacity)]; }
        public int Row(int index)=>m_Heap[index].Row;
        public EntityHandle Handle(int index)=>m_Heap[index].Handle;
        public void Begin(float2 focus,int budget) { m_Focus=focus;m_Budget=math.clamp(budget,0,m_Heap.Length);Count=0; }
        public void Consider(EntityHandle handle,float2 position,int row)
        {
            if(m_Budget==0||!math.all(math.isfinite(position)))return;
            // Quarter-unit squared bands avoid tiny interpolated motion churning the boundary.
            var value=new Candidate { Row=row,Handle=handle,Distance=math.floor(math.distancesq(position,m_Focus)*4f) };
            if(Count<m_Budget)
            {
                int i=Count++;m_Heap[i]=value;
                while(i>0){int p=(i-1)/2;if(!Worse(m_Heap[i],m_Heap[p]))break;Swap(i,p);i=p;}
            }
            else if(Worse(m_Heap[0],value))
            {
                m_Heap[0]=value;int i=0;
                while(true){int l=i*2+1;if(l>=Count)break;int r=l+1,w=r<Count&&Worse(m_Heap[r],m_Heap[l])?r:l;if(!Worse(m_Heap[w],m_Heap[i]))break;Swap(w,i);i=w;}
            }
        }
        static bool Worse(Candidate a,Candidate b)=>a.Distance>b.Distance||(a.Distance==b.Distance&&(a.Handle.Index>b.Handle.Index||(a.Handle.Index==b.Handle.Index&&a.Handle.Generation>b.Handle.Generation)));
        void Swap(int a,int b){var v=m_Heap[a];m_Heap[a]=m_Heap[b];m_Heap[b]=v;}
    }
}

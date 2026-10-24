using System;
using System.Collections.Generic;
using System.Threading;

namespace FreeNormalFinder {
// Each yielded item is one committed search unit; null means a non-matching unit.
// The enumerator stays alive only in this application session.
public sealed class ResumableSearch<T>:IDisposable where T:class {
    readonly IEnumerator<T> steps;readonly List<T> rows=new List<T>();readonly int limit;long processed;int running;
    public volatile bool StopRequested;public bool Complete {get;private set;}public bool LimitReached {get;private set;}public long Total {get;private set;}
    public long Processed {get{return Interlocked.Read(ref processed);}}public int Count {get{return rows.Count;}}
    public ResumableSearch(IEnumerable<T> units,long total,int limit=100000){if(units==null||total<0||limit<1||limit>100000)throw new ArgumentException("検索範囲・上限が不正です。");steps=units.GetEnumerator();Total=total;this.limit=limit;}
    public void PrepareResume(){StopRequested=false;}
    public void Run(){if(Interlocked.Exchange(ref running,1)!=0)throw new InvalidOperationException("検索は既に実行中です。");try{if(Complete)return;if(rows.Count>=limit){LimitReached=true;return;}while(!StopRequested){if(!steps.MoveNext()){Complete=true;return;}var row=steps.Current;if(row!=null)rows.Add(row);Interlocked.Increment(ref processed);if(rows.Count>=limit){if(Processed>=Total)Complete=true;else LimitReached=true;return;}}}finally{Interlocked.Exchange(ref running,0);}}
    public T[] Snapshot(){if(Interlocked.CompareExchange(ref running,0,0)!=0)throw new InvalidOperationException("中断してから結果を取得してください。");return rows.ToArray();}
    public void Dispose(){if(Interlocked.CompareExchange(ref running,0,0)!=0)throw new InvalidOperationException("実行中の検索は破棄できません。");steps.Dispose();}
}
}

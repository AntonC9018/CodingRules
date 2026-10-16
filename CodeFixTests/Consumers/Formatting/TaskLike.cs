using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

public class TaskLikeConsumer
{
    public async MyTask<int> Method() => await Task.FromResult(
        1);

    public MyTask<int> LocalFunction()
    {
        async MyTask<int> Local() => await Task.FromResult(
            2);
        return Local();
    }
}

[AsyncMethodBuilder(typeof(MyTaskMethodBuilder<>))]
public struct MyTask<T>
{
    public Task<T> Wrapped;
    public TaskAwaiter<T> GetAwaiter() => Wrapped.GetAwaiter();
}

public struct MyTaskMethodBuilder<T>
{
    private AsyncTaskMethodBuilder<T> inner;

    public static MyTaskMethodBuilder<T> Create() => new MyTaskMethodBuilder<T> { inner = AsyncTaskMethodBuilder<T>.Create() };
    public MyTask<T> Task => new MyTask<T> { Wrapped = inner.Task };
    public void SetResult(T result) => inner.SetResult(result);
    public void SetException(Exception exception) => inner.SetException(exception);
    public void SetStateMachine(IAsyncStateMachine stateMachine) => inner.SetStateMachine(stateMachine);
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => inner.Start(ref stateMachine);
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => inner.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => inner.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}

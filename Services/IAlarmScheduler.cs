using System;

namespace Reminder;

// Where alarms are registered so they fire while the app is closed.
// WindowsAlarmScheduler uses the Windows Task Scheduler; tests use a fake.
public interface IAlarmScheduler
{
    bool IsStartupEnabled();
    void Set(TaskItem task);
    void Remove(Guid id);
    void SetStartup(bool enabled);
}

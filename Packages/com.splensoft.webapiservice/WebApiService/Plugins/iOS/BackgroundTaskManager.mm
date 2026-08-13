// BackgroundTaskManager.mm
// Provides iOS background execution support for the SerializedNetworkQueue.
// Uses UIApplication background tasks (up to ~30 s of extra time after suspension)
// and registers a BGProcessingTask so iOS can schedule longer runs while the
// device is idle / charging.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <BackgroundTasks/BackgroundTasks.h>

// ── BGTask identifier ────────────────────────────────────────────────────────
// Must match the value added to Info.plist BGTaskSchedulerPermittedIdentifiers
// and the identifier passed from C# via SNQ_ScheduleBGProcessingTask().
static NSString * const kBGProcessingTaskIdentifier =
    @"com.splensoft.webapiservice.networkqueue";

// ── UIApplication background-task handle ────────────────────────────────────
static UIBackgroundTaskIdentifier _bgTaskID = UIBackgroundTaskInvalid;

// ── BGProcessingTask handle (kept so we can call setTaskCompleted) ───────────
static BGTask *_activeBGTask API_AVAILABLE(ios(13.0)) = nil;

// ── C → Unity callback bridge ────────────────────────────────────────────────
// Unity calls SNQ_SetBGTaskExpiredCallback to register a void(*)() that will be
// invoked when iOS is about to kill the background time.
typedef void (*SNQ_BackgroundExpiredCallback)(void);
static SNQ_BackgroundExpiredCallback _expiredCallback = NULL;

extern "C"
{

// ── Called by C# when the app moves to background ────────────────────────────
void SNQ_BeginBackgroundTask(void)
{
    if (_bgTaskID != UIBackgroundTaskInvalid)
        return; // already running

    _bgTaskID = [[UIApplication sharedApplication]
        beginBackgroundTaskWithName:@"SplenSoft.NetworkQueue"
        expirationHandler:^{
            // iOS is about to suspend us – notify C# so the queue can be
            // checkpointed, then end the task.
            if (_expiredCallback)
                _expiredCallback();

            [[UIApplication sharedApplication]
                endBackgroundTask:_bgTaskID];
            _bgTaskID = UIBackgroundTaskInvalid;
        }];
}

// ── Called by C# when the app returns to foreground ──────────────────────────
void SNQ_EndBackgroundTask(void)
{
    if (_bgTaskID == UIBackgroundTaskInvalid)
        return;

    [[UIApplication sharedApplication] endBackgroundTask:_bgTaskID];
    _bgTaskID = UIBackgroundTaskInvalid;
}

// ── Register the expiration callback from C# ─────────────────────────────────
void SNQ_SetBGTaskExpiredCallback(SNQ_BackgroundExpiredCallback callback)
{
    _expiredCallback = callback;
}

// ── Returns remaining background time in seconds (-1 on < iOS 7) ─────────────
double SNQ_GetRemainingBackgroundTime(void)
{
    return (double)[[UIApplication sharedApplication]
        backgroundTimeRemaining];
}

// ── BGTaskScheduler integration (iOS 13+) ─────────────────────────────────────
// Register the processing task handler.  Call this once at app start (from
// AppDelegate or from C# via SNQ_RegisterBGProcessingTask).  Unity calls this
// before the first scene loads via the C# BackgroundTaskManager.Awake().
void SNQ_RegisterBGProcessingTask(const char *identifier)
{
    if (@available(iOS 13.0, *))
    {
        NSString *taskID = identifier
            ? [NSString stringWithUTF8String:identifier]
            : kBGProcessingTaskIdentifier;

        [[BGTaskScheduler sharedScheduler]
            registerForTaskWithIdentifier:taskID
            usingQueue:dispatch_get_main_queue()
            launchHandler:^(__kindof BGTask *task) {
                _activeBGTask = task;

                // Tell iOS we need network access and that this may take a while.
                // The actual queue processing happens on the Unity side; we just
                // keep a reference so C# can call SNQ_CompleteBGProcessingTask().
                task.expirationHandler = ^{
                    if (_expiredCallback)
                        _expiredCallback();
                    [task setTaskCompletedWithSuccess:NO];
                    _activeBGTask = nil;
                };
            }];
    }
}

// ── Schedule the next BGProcessingTask run ────────────────────────────────────
void SNQ_ScheduleBGProcessingTask(const char *identifier)
{
    if (@available(iOS 13.0, *))
    {
        NSString *taskID = identifier
            ? [NSString stringWithUTF8String:identifier]
            : kBGProcessingTaskIdentifier;

        BGProcessingTaskRequest *request =
            [[BGProcessingTaskRequest alloc] initWithIdentifier:taskID];
        request.requiresNetworkConnectivity = YES;
        request.requiresExternalPower       = NO;
        // Earliest begin: 1 minute from now (system may defer further)
        request.earliestBeginDate = [NSDate dateWithTimeIntervalSinceNow:60];

        NSError *error = nil;
        [[BGTaskScheduler sharedScheduler] submitTaskRequest:request
                                                       error:&error];
        if (error)
            NSLog(@"[SNQ] Failed to schedule BGProcessingTask: %@", error);
        else
            NSLog(@"[SNQ] Scheduled BGProcessingTask: %@", taskID);
    }
}

// ── Signal that the queue has finished processing (call from C#) ──────────────
void SNQ_CompleteBGProcessingTask(bool success)
{
    if (@available(iOS 13.0, *))
    {
        if (_activeBGTask)
        {
            [_activeBGTask setTaskCompletedWithSuccess:success];
            _activeBGTask = nil;
        }
    }
}

} // extern "C"

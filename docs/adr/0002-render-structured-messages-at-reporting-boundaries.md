# Render structured messages at reporting boundaries

For the compound reporting messages raised in ScheduleLib #229 A6, prefer a
named record struct containing message data, passed to a generic reporting
boundary that renders it where needed. This keeps message construction out of
the call site and supports localization better than either inline formatting
or merely naming the completed string; the reporting constraint, including
whether to use `ISpanFormattable`, remains an implementation question rather
than a universal interface requirement.

Automatic fixes must preserve evaluation and formatting behavior, including
handler gating, culture, overload selection, and observations of mutable data.
The exact scope beyond the cited reporting examples remains open in the
[follow-up questions](../schedulelib-follow-up.md#open-design-questions).

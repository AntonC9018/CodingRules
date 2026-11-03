# Separate confident findings from automatic fix availability

For new and revised rules, confidently identifying a violation may justify a
diagnostic even when its refactoring requires human judgment; silently accepting
the code merely because a fix is unavailable would hide a real finding. Offer
multiple safe actions where useful, and preserve behavior in every action;
the original CR0001 safe-fix requirement and existing families' documented
contracts remain in force until explicitly revised.

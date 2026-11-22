.text

.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
RhpAssignRefArm64:
RhpCheckedAssignRefArm64:
    str     x15, [x14], #8
    ret

.global RhpByRefAssignRefArm64
RhpByRefAssignRefArm64:
    ldr     x15, [x13], #8
    str     x15, [x14], #8
    ret
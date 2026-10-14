package com.bubu.android

import org.junit.Assert.assertEquals
import org.junit.Test

class AttributionTest {
    @Test fun nameAndRepoMatchPermission() {
        assertEquals("Bubu for Android", Attribution.APP_NAME)
        assertEquals("https://github.com/SahrulRamadhani/bubu", Attribution.REPO_URL)
    }
}

package com.bubu.android.app

import android.app.Application

class BubuApp : Application() {
    val model: AppModel by lazy { AppModel(this) }
}
